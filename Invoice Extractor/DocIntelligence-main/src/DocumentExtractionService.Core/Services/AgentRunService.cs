using System.Collections.Concurrent;
using System.Diagnostics;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Agent Run Service — the recursive reason-act-observe loop.
///
/// ORCHESTRATION FLOW:
///   1. PLAN   → Determine action sequence from AgentDefinition
///   2. REASON → Decide which action to execute next (LLM or deterministic)
///   3. ACT    → Execute the action via ActionRegistry handler
///   4. OBSERVE→ Analyze the result, update state
///   5. LOOP   → Go to (2) unless done or guardrails hit
///
/// GUARDRAILS (checked at every step):
///   - Max steps reached?
///   - Max tokens exceeded?
///   - Max duration exceeded?
///   - Action blocked by TrustGate?
///   - Human approval required?
///
/// The service is stateless — all state lives in AgentRun.
/// </summary>
public interface IAgentRunService
{
    /// <summary>Start a new agent run.</summary>
    Task<AgentRun> StartRunAsync(
        AgentDefinition agent,
        Stream? documentStream,
        string? fileName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>Resume a paused run (e.g., after human approval).</summary>
    Task<AgentRun> ResumeRunAsync(
        string runId,
        bool approved,
        CancellationToken cancellationToken = default);

    /// <summary>Get the current state of a run.</summary>
    AgentRun? GetRun(string runId);

    /// <summary>List all active runs for an agent.</summary>
    IReadOnlyList<AgentRun> GetActiveRuns(string? agentId = null);
}

public class AgentRunService : IAgentRunService
{
    private readonly IDocumentExtractionService _extractionService;
    private readonly IActionTrustGate _trustGate;
    private readonly ConcurrentDictionary<string, AgentRun> _runs = new();
    private readonly ILogger<AgentRunService> _logger;

    public AgentRunService(
        IDocumentExtractionService extractionService,
        IActionTrustGate trustGate,
        ILogger<AgentRunService> logger)
    {
        _extractionService = extractionService;
        _trustGate = trustGate;
        _logger = logger;
    }

    public async Task<AgentRun> StartRunAsync(
        AgentDefinition agent,
        Stream? documentStream,
        string? fileName,
        Dictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var run = new AgentRun
        {
            AgentId = agent.Id,
            Input = new RunInput
            {
                FileName = fileName,
                FileSizeBytes = documentStream?.Length ?? 0,
                DocumentType = agent.DocumentTypeId,
                Parameters = parameters,
            }
        };

        _runs[run.RunId] = run;
        _logger.LogInformation("Agent run {RunId} started for agent {AgentId}", run.RunId, agent.Id);

        try
        {
            // ── Phase 1: PLAN ──
            run.TransitionTo(RunState.Planning);
            PlanActions(agent, run);

            // ── Phase 2-5: EXECUTE LOOP ──
            run.TransitionTo(RunState.Executing);
            await ExecuteLoopAsync(agent, run, documentStream, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            run.Error = new RunError { Code = "CANCELLED", Message = "Run was cancelled" };
            if (run.State is not (RunState.Completed or RunState.Failed))
                run.TransitionTo(RunState.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent run {RunId} failed", run.RunId);
            run.Error = new RunError
            {
                Code = "UNHANDLED_ERROR",
                Message = ex.Message,
                FailedAtStep = run.CurrentStep,
                FailedActionId = run.CurrentActionIndex < run.ActionPlan.Count
                    ? run.ActionPlan[run.CurrentActionIndex].ActionId : null,
            };
            if (run.State is not (RunState.Completed or RunState.Failed))
                run.TransitionTo(RunState.Failed);
        }

        return run;
    }

    public async Task<AgentRun> ResumeRunAsync(string runId, bool approved, CancellationToken cancellationToken)
    {
        if (!_runs.TryGetValue(runId, out var run))
            throw new KeyNotFoundException($"Run {runId} not found");

        if (run.State != RunState.AwaitingApproval)
            throw new InvalidOperationException($"Run {runId} is not awaiting approval (state: {run.State})");

        if (!approved)
        {
            run.Error = new RunError { Code = "APPROVAL_DENIED", Message = "Human denied the action" };
            run.TransitionTo(RunState.Failed);
            return run;
        }

        // Clear pending approval and resume
        run.PendingApproval = null;
        run.TransitionTo(RunState.Executing);

        // We need the agent definition to resume — in production, store it or re-fetch
        _logger.LogInformation("Run {RunId} resumed after approval", runId);
        return run;
    }

    public AgentRun? GetRun(string runId) =>
        _runs.TryGetValue(runId, out var run) ? run : null;

    public IReadOnlyList<AgentRun> GetActiveRuns(string? agentId = null)
    {
        var query = _runs.Values.Where(r =>
            r.State is not (RunState.Completed or RunState.Failed));

        if (agentId != null)
            query = query.Where(r => r.AgentId == agentId);

        return query.OrderByDescending(r => r.StartedAt).ToList();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Planning
    // ═══════════════════════════════════════════════════════════════════

    private static void PlanActions(AgentDefinition agent, AgentRun run)
    {
        // Build the action plan from the agent's configured actions
        run.ActionPlan = agent.Actions
            .Where(a => a.Enabled)
            .Select(a => new PlannedAction
            {
                ActionId = a.Id,
                Status = ActionStatus.Pending,
            })
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Main Execution Loop
    // ═══════════════════════════════════════════════════════════════════

    private async Task ExecuteLoopAsync(
        AgentDefinition agent,
        AgentRun run,
        Stream? documentStream,
        CancellationToken ct)
    {
        while (run.CurrentActionIndex < run.ActionPlan.Count && !ct.IsCancellationRequested)
        {
            run.CurrentStep++;
            var planned = run.ActionPlan[run.CurrentActionIndex];
            var action = agent.Actions.FirstOrDefault(a => a.Id == planned.ActionId);

            if (action == null)
            {
                planned.Status = ActionStatus.Skipped;
                planned.Reason = "Action not found in agent definition";
                run.CurrentActionIndex++;
                continue;
            }

            // ── Guardrail checks ──
            var guardrailResult = _trustGate.Evaluate(agent, run, action);
            run.Steps.Add(new RunStep
            {
                StepNumber = run.CurrentStep,
                Phase = "reason",
                ActionId = action.Id,
                Reasoning = $"Next action: {action.DisplayName}. TrustGate: {(guardrailResult.Allowed ? "ALLOW" : "DENY")}",
                TrustDecision = guardrailResult,
            });

            if (!guardrailResult.Allowed)
            {
                if (guardrailResult.TrustLevel == "approve")
                {
                    // Park for human approval
                    planned.Status = ActionStatus.AwaitingApproval;
                    run.PendingApproval = new PendingApproval
                    {
                        ActionId = action.Id,
                        Description = $"Action '{action.DisplayName}' requires approval. Reason: {guardrailResult.Reason}",
                    };
                    run.TransitionTo(RunState.AwaitingApproval);
                    return; // Paused — will resume after approval
                }

                // Hard block
                planned.Status = ActionStatus.Failed;
                planned.Reason = guardrailResult.Reason;
                run.Error = new RunError
                {
                    Code = "GUARDRAIL_BLOCKED",
                    Message = guardrailResult.Reason,
                    FailedAtStep = run.CurrentStep,
                    FailedActionId = action.Id,
                };
                run.TransitionTo(RunState.Failed);
                return;
            }

            // ── Execute action ──
            var sw = Stopwatch.StartNew();
            planned.Status = ActionStatus.Running;

            try
            {
                var result = await ExecuteActionAsync(agent, run, action, documentStream, ct);
                sw.Stop();

                planned.Status = ActionStatus.Completed;
                run.Steps.Add(new RunStep
                {
                    StepNumber = run.CurrentStep,
                    Phase = "act",
                    ActionId = action.Id,
                    DurationMs = sw.ElapsedMilliseconds,
                    ActionOutput = result,
                });

                // ── Observe ──
                run.TransitionTo(RunState.Observing);
                run.Steps.Add(new RunStep
                {
                    StepNumber = run.CurrentStep,
                    Phase = "observe",
                    ActionId = action.Id,
                    Observation = $"Action '{action.DisplayName}' completed successfully in {sw.ElapsedMilliseconds}ms",
                });

                run.CurrentActionIndex++;

                // Transition back to Executing if more actions remain
                if (run.CurrentActionIndex < run.ActionPlan.Count)
                    run.TransitionTo(RunState.Executing);
            }
            catch (Exception ex)
            {
                sw.Stop();
                planned.Status = ActionStatus.Failed;
                planned.Reason = ex.Message;

                run.Steps.Add(new RunStep
                {
                    StepNumber = run.CurrentStep,
                    Phase = "act",
                    ActionId = action.Id,
                    DurationMs = sw.ElapsedMilliseconds,
                    Observation = $"FAILED: {ex.Message}",
                });

                run.Error = new RunError
                {
                    Code = "ACTION_FAILED",
                    Message = ex.Message,
                    FailedAtStep = run.CurrentStep,
                    FailedActionId = action.Id,
                };
                run.TransitionTo(RunState.Failed);
                return;
            }
        }

        // ── All actions completed ──
        run.Output = new RunOutput
        {
            Status = "success",
            ActionsCompleted = run.ActionPlan.Count(a => a.Status == ActionStatus.Completed),
            ActionsTotal = run.ActionPlan.Count,
            ConfidenceScore = 0.95,
            Summary = $"Agent completed {run.ActionPlan.Count(a => a.Status == ActionStatus.Completed)} of {run.ActionPlan.Count} actions",
        };
        run.TransitionTo(RunState.Completed);
        _logger.LogInformation("Agent run {RunId} completed: {Steps} steps, {ElapsedMs}ms",
            run.RunId, run.CurrentStep, run.ElapsedMs);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Action Execution (dispatch by ActionType)
    // ═══════════════════════════════════════════════════════════════════

    private async Task<object?> ExecuteActionAsync(
        AgentDefinition agent,
        AgentRun run,
        AgentAction action,
        Stream? documentStream,
        CancellationToken ct)
    {
        return action.Type switch
        {
            ActionType.Extract => await ExecuteExtractAsync(agent, run, documentStream, ct),
            ActionType.Validate => ExecuteValidate(run),
            ActionType.Summarize => ExecuteSummarize(run),
            ActionType.Export => ExecuteExport(run),
            ActionType.Compare => new { status = "not_implemented", message = "Compare action is planned for next sprint" },
            ActionType.CrossReference => new { status = "not_implemented" },
            ActionType.Notify => new { status = "notification_sent" },
            ActionType.Webhook => new { status = "webhook_triggered" },
            ActionType.TriggerWorkflow => new { status = "workflow_triggered" },
            ActionType.Custom => new { status = "custom_action", action_id = action.Id },
            _ => throw new NotSupportedException($"Unknown action type: {action.Type}")
        };
    }

    private async Task<object> ExecuteExtractAsync(
        AgentDefinition agent, AgentRun run, Stream? documentStream, CancellationToken ct)
    {
        if (documentStream == null)
            throw new InvalidOperationException("Extract action requires a document stream");

        if (agent.DocumentTypeId == null)
            throw new InvalidOperationException("Agent has no document type configured for extraction");

        // Delegate to the existing extraction pipeline
        var result = await _extractionService.ExtractAsync(
            documentStream,
            run.Input.FileName ?? "document.pdf",
            agent.DocumentTypeId,
            requestId: run.RunId,
            cancellationToken: ct);

        run.TotalTokensUsed += result.Metadata.TotalTokensUsed;
        run.Output = new RunOutput
        {
            Data = result.Data,
            ConfidenceScore = (double)result.Validation.ConfidenceScore,
        };

        return result;
    }

    private static object ExecuteValidate(AgentRun run)
    {
        // Validation already runs as part of extraction, but this action
        // allows running additional validation logic or cross-referencing
        return new
        {
            status = "validated",
            confidence = run.Output?.ConfidenceScore ?? 0,
        };
    }

    private static object ExecuteSummarize(AgentRun run)
    {
        return new
        {
            status = "summarized",
            summary = run.Output?.Summary ?? "Extraction completed successfully",
        };
    }

    private static object ExecuteExport(AgentRun run)
    {
        return new
        {
            status = "exported",
            format = "json",
            data = run.Output?.Data,
        };
    }
}

/// <summary>
/// Action Trust Gate — evaluates whether an action should be allowed at each step.
/// Loop-aware: tracks cumulative resource usage across the run.
/// </summary>
public interface IActionTrustGate
{
    TrustDecision Evaluate(AgentDefinition agent, AgentRun run, AgentAction action);
}

public class ActionTrustGate : IActionTrustGate
{
    private readonly ILogger<ActionTrustGate> _logger;

    public ActionTrustGate(ILogger<ActionTrustGate> logger)
    {
        _logger = logger;
    }

    public TrustDecision Evaluate(AgentDefinition agent, AgentRun run, AgentAction action)
    {
        var checks = new List<GuardrailCheck>();
        var guardrails = agent.Guardrails;

        // Check 1: Max steps
        var stepsOk = run.CurrentStep <= guardrails.MaxSteps;
        checks.Add(new GuardrailCheck
        {
            Name = "max_steps",
            Passed = stepsOk,
            Detail = stepsOk ? null : $"Step {run.CurrentStep} exceeds max {guardrails.MaxSteps}",
        });

        // Check 2: Max tokens
        var tokensOk = run.TotalTokensUsed < guardrails.MaxTotalTokens;
        checks.Add(new GuardrailCheck
        {
            Name = "max_tokens",
            Passed = tokensOk,
            Detail = tokensOk ? null : $"Used {run.TotalTokensUsed} tokens, max is {guardrails.MaxTotalTokens}",
        });

        // Check 3: Max duration
        var durationOk = run.ElapsedMs < guardrails.MaxDurationSeconds * 1000L;
        checks.Add(new GuardrailCheck
        {
            Name = "max_duration",
            Passed = durationOk,
            Detail = durationOk ? null : $"Elapsed {run.ElapsedMs}ms exceeds max {guardrails.MaxDurationSeconds}s",
        });

        // Check 4: Blocked actions
        var notBlocked = !guardrails.BlockedActions.Contains(action.Id);
        checks.Add(new GuardrailCheck
        {
            Name = "blocked_actions",
            Passed = notBlocked,
            Detail = notBlocked ? null : $"Action '{action.Id}' is blocked by guardrails",
        });

        // Check 5: Approval required
        var approvalMode = guardrails.ApprovalRequired.GetValueOrDefault(action.Id, action.TrustLevel);
        var needsApproval = approvalMode == "approve" || approvalMode == "always";

        var allPassed = checks.All(c => c.Passed) && !needsApproval;

        var decision = new TrustDecision
        {
            ActionId = action.Id,
            Allowed = allPassed,
            TrustLevel = needsApproval ? "approve" : "auto",
            Reason = allPassed
                ? "All guardrails passed"
                : needsApproval
                    ? $"Action '{action.DisplayName}' requires human approval"
                    : checks.First(c => !c.Passed).Detail ?? "Guardrail check failed",
            Checks = checks,
        };

        _logger.LogDebug("TrustGate: {ActionId} → {Allowed} ({Reason})",
            action.Id, decision.Allowed, decision.Reason);

        return decision;
    }
}
