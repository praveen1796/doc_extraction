using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

/// <summary>
/// RunState — the state machine for a single agent run.
///
/// STATE FLOW:
///   Created → Planning → Executing → [Observing ↔ Executing]* → Completed
///                  ↓          ↓                ↓                      ↓
///               Failed     Failed           Failed              (terminal)
///                  ↓          ↓                ↓
///           AwaitingApproval (if TrustGate requires human-in-loop)
///
/// Each cycle through Executing → Observing is one "step" in the reason-act-observe loop.
/// The guardrails in AgentDefinition enforce max steps, max tokens, and max duration.
/// </summary>
public class AgentRun
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("agent_id")]
    public string AgentId { get; set; } = "";

    [JsonPropertyName("state")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public RunState State { get; set; } = RunState.Created;

    [JsonPropertyName("current_step")]
    public int CurrentStep { get; set; }

    [JsonPropertyName("total_tokens_used")]
    public int TotalTokensUsed { get; set; }

    [JsonPropertyName("started_at")]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs => CompletedAt.HasValue
        ? (long)(CompletedAt.Value - StartedAt).TotalMilliseconds
        : (long)(DateTime.UtcNow - StartedAt).TotalMilliseconds;

    // ── Action Plan ───────────────────────────────────────────────────────
    /// <summary>
    /// The planned sequence of actions. Set during Planning phase.
    /// May be modified during Observing phase if AllowDynamicPlanning is true.
    /// </summary>
    [JsonPropertyName("action_plan")]
    public List<PlannedAction> ActionPlan { get; set; } = [];

    [JsonPropertyName("current_action_index")]
    public int CurrentActionIndex { get; set; }

    // ── Step Log (the audit trail) ────────────────────────────────────────
    [JsonPropertyName("steps")]
    public List<RunStep> Steps { get; set; } = [];

    // ── Input / Output ────────────────────────────────────────────────────
    [JsonPropertyName("input")]
    public RunInput Input { get; set; } = new();

    [JsonPropertyName("output")]
    public RunOutput? Output { get; set; }

    // ── Error ─────────────────────────────────────────────────────────────
    [JsonPropertyName("error")]
    public RunError? Error { get; set; }

    // ── Trust Gate ────────────────────────────────────────────────────────
    /// <summary>
    /// If State == AwaitingApproval, this describes what needs approval.
    /// </summary>
    [JsonPropertyName("pending_approval")]
    public PendingApproval? PendingApproval { get; set; }

    // ── State transitions ─────────────────────────────────────────────────
    public void TransitionTo(RunState newState)
    {
        // Validate legal transitions
        var legal = State switch
        {
            RunState.Created => newState is RunState.Planning or RunState.Failed,
            RunState.Planning => newState is RunState.Executing or RunState.Failed,
            RunState.Executing => newState is RunState.Observing or RunState.AwaitingApproval or RunState.Completed or RunState.Failed,
            RunState.Observing => newState is RunState.Executing or RunState.Completed or RunState.Failed,
            RunState.AwaitingApproval => newState is RunState.Executing or RunState.Failed,
            _ => false,
        };

        if (!legal)
            throw new InvalidOperationException($"Invalid state transition: {State} → {newState}");

        State = newState;
        if (newState is RunState.Completed or RunState.Failed)
            CompletedAt = DateTime.UtcNow;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RunState
{
    Created,
    Planning,
    Executing,
    Observing,
    AwaitingApproval,
    Completed,
    Failed
}

/// <summary>
/// A planned action in the agent's execution plan.
/// </summary>
public class PlannedAction
{
    [JsonPropertyName("action_id")]
    public string ActionId { get; set; } = "";

    [JsonPropertyName("status")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionStatus Status { get; set; } = ActionStatus.Pending;

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionStatus
{
    Pending,
    Running,
    Completed,
    Skipped,
    Failed,
    AwaitingApproval
}

/// <summary>
/// A single step in the reason-act-observe loop.
/// </summary>
public class RunStep
{
    [JsonPropertyName("step_number")]
    public int StepNumber { get; set; }

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = ""; // "reason", "act", "observe"

    [JsonPropertyName("action_id")]
    public string? ActionId { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; set; }

    [JsonPropertyName("tokens_used")]
    public int TokensUsed { get; set; }

    /// <summary>
    /// The reasoning output — why the agent chose this action.
    /// Only present for "reason" phase steps.
    /// </summary>
    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }

    /// <summary>
    /// The action's input (what was passed to the handler).
    /// </summary>
    [JsonPropertyName("action_input")]
    public object? ActionInput { get; set; }

    /// <summary>
    /// The action's output (what the handler returned).
    /// </summary>
    [JsonPropertyName("action_output")]
    public object? ActionOutput { get; set; }

    /// <summary>
    /// The observation — what the agent learned from the action result.
    /// Only present for "observe" phase steps.
    /// </summary>
    [JsonPropertyName("observation")]
    public string? Observation { get; set; }

    /// <summary>Trust gate decision for this step.</summary>
    [JsonPropertyName("trust_decision")]
    public TrustDecision? TrustDecision { get; set; }
}

/// <summary>
/// Trust gate decision record — logged for every action attempt.
/// </summary>
public class TrustDecision
{
    [JsonPropertyName("action_id")]
    public string ActionId { get; set; } = "";

    [JsonPropertyName("allowed")]
    public bool Allowed { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("trust_level")]
    public string TrustLevel { get; set; } = "auto";

    /// <summary>Guardrail checks that were evaluated.</summary>
    [JsonPropertyName("checks")]
    public List<GuardrailCheck> Checks { get; set; } = [];
}

public class GuardrailCheck
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("passed")]
    public bool Passed { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}

public class PendingApproval
{
    [JsonPropertyName("action_id")]
    public string ActionId { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("requested_at")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>What the agent received as input.</summary>
public class RunInput
{
    [JsonPropertyName("file_name")]
    public string? FileName { get; set; }

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("document_type")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("additional_context")]
    public string? AdditionalContext { get; set; }

    [JsonPropertyName("parameters")]
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>The final output of an agent run.</summary>
public class RunOutput
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = ""; // "success", "partial", "failed"

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("confidence_score")]
    public double ConfidenceScore { get; set; }

    [JsonPropertyName("actions_completed")]
    public int ActionsCompleted { get; set; }

    [JsonPropertyName("actions_total")]
    public int ActionsTotal { get; set; }
}

public class RunError
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("step")]
    public int? FailedAtStep { get; set; }

    [JsonPropertyName("action_id")]
    public string? FailedActionId { get; set; }
}
