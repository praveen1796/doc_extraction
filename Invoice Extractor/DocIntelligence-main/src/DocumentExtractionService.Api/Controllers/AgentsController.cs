using System.Text.Json.Serialization;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentExtractionService.Api.Controllers;

/// <summary>
/// Agent Store API — list agents, start runs, check status, approve actions.
///
/// ENDPOINTS:
/// GET    /api/v1/agents              → List all agents
/// GET    /api/v1/agents/{id}         → Get agent details
/// POST   /api/v1/agents/{id}/run     → Start an agent run (upload document)
/// GET    /api/v1/agents/runs/{runId} → Get run status
/// POST   /api/v1/agents/runs/{runId}/approve → Approve a pending action
/// GET    /api/v1/agents/runs         → List all active runs
/// </summary>
[ApiController]
[Route("api/v1/agents")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IAgentStore _agentStore;
    private readonly IAgentRunService _runService;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(
        IAgentStore agentStore,
        IAgentRunService runService,
        ILogger<AgentsController> logger)
    {
        _agentStore = agentStore;
        _runService = runService;
        _logger = logger;
    }

    /// <summary>List all available agents.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AgentSummary[]), StatusCodes.Status200OK)]
    public ActionResult<AgentSummary[]> GetAll()
    {
        var agents = _agentStore.GetAllAgents()
            .Select(MapToSummary)
            .ToArray();
        return Ok(agents);
    }

    /// <summary>Get detailed agent definition.</summary>
    [HttpGet("{agentId}")]
    [ProducesResponseType(typeof(AgentDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AgentDetail> GetById(string agentId)
    {
        var agent = _agentStore.GetAgent(agentId);
        if (agent == null)
            return NotFound(Problem(title: "Agent not found", detail: $"No agent with ID '{agentId}'.", statusCode: 404));
        return Ok(MapToDetail(agent));
    }

    /// <summary>
    /// Start an agent run — upload a document and let the agent process it.
    /// </summary>
    [HttpPost("{agentId}/run")]
    [RequestSizeLimit(52_428_800)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(AgentRun), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentRun>> StartRun(
        string agentId,
        [FromForm] IFormFile? file,
        [FromForm] string? parametersJson = null,
        CancellationToken cancellationToken = default)
    {
        var agent = _agentStore.GetAgent(agentId);
        if (agent == null)
            return NotFound(Problem(title: "Agent not found", detail: $"No agent with ID '{agentId}'.", statusCode: 404));

        Stream? docStream = null;
        string? fileName = null;
        if (file != null && file.Length > 0)
        {
            docStream = file.OpenReadStream();
            fileName = file.FileName;
        }

        Dictionary<string, object>? parameters = null;
        if (!string.IsNullOrEmpty(parametersJson))
        {
            try
            {
                parameters = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, object>>(parametersJson);
            }
            catch { /* ignore malformed params */ }
        }

        var run = await _runService.StartRunAsync(agent, docStream, fileName, parameters, cancellationToken);

        _logger.LogInformation("Agent run {RunId} started for {AgentId}: state={State}",
            run.RunId, agentId, run.State);

        return AcceptedAtAction(nameof(GetRunStatus), new { runId = run.RunId }, run);
    }

    /// <summary>Get the status of an agent run.</summary>
    [HttpGet("runs/{runId}")]
    [ProducesResponseType(typeof(AgentRun), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<AgentRun> GetRunStatus(string runId)
    {
        var run = _runService.GetRun(runId);
        if (run == null)
            return NotFound(Problem(title: "Run not found", detail: $"No run with ID '{runId}'.", statusCode: 404));
        return Ok(run);
    }

    /// <summary>Approve or deny a pending action in a paused run.</summary>
    [HttpPost("runs/{runId}/approve")]
    [ProducesResponseType(typeof(AgentRun), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentRun>> ApproveAction(
        string runId,
        [FromBody] ApprovalRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await _runService.ResumeRunAsync(runId, request.Approved, cancellationToken);
            return Ok(run);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(Problem(title: "Run not found", detail: $"No run with ID '{runId}'.", statusCode: 404));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(Problem(title: "Invalid operation", detail: ex.Message, statusCode: 400));
        }
    }

    /// <summary>List all active (non-terminal) runs.</summary>
    [HttpGet("runs")]
    [ProducesResponseType(typeof(AgentRun[]), StatusCodes.Status200OK)]
    public ActionResult<AgentRun[]> GetActiveRuns([FromQuery] string? agentId = null)
    {
        var runs = _runService.GetActiveRuns(agentId);
        return Ok(runs);
    }

    // ── DTOs ──

    private static AgentSummary MapToSummary(AgentDefinition a) => new()
    {
        Id = a.Id,
        DisplayName = a.DisplayName,
        Description = a.Description,
        Version = a.Version,
        Enabled = a.Enabled,
        IconName = a.IconName,
        Category = a.Category,
        Tags = a.Tags,
        DocumentTypeId = a.DocumentTypeId,
        ActionCount = a.Actions.Count(x => x.Enabled),
        ActionNames = a.Actions.Where(x => x.Enabled).Select(x => x.DisplayName).ToList(),
        HasGuardrails = a.Guardrails.MaxSteps < 100 || a.Guardrails.ApprovalRequired.Count > 0,
        IsAutoGenerated = a.Tags.Contains("auto-generated"),
    };

    private static AgentDetail MapToDetail(AgentDefinition a) => new()
    {
        Id = a.Id,
        DisplayName = a.DisplayName,
        Description = a.Description,
        Version = a.Version,
        Enabled = a.Enabled,
        IconName = a.IconName,
        Category = a.Category,
        Tags = a.Tags,
        DocumentTypeId = a.DocumentTypeId,
        Actions = a.Actions,
        Guardrails = a.Guardrails,
        Triggers = a.Triggers,
        Output = a.Output,
        IsAutoGenerated = a.Tags.Contains("auto-generated"),
    };
}

// ── DTOs ──────────────────────────────────────────────────────────

public class AgentSummary
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("icon_name")] public string? IconName { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = [];
    [JsonPropertyName("document_type_id")] public string? DocumentTypeId { get; set; }
    [JsonPropertyName("action_count")] public int ActionCount { get; set; }
    [JsonPropertyName("action_names")] public List<string> ActionNames { get; set; } = [];
    [JsonPropertyName("has_guardrails")] public bool HasGuardrails { get; set; }
    [JsonPropertyName("is_auto_generated")] public bool IsAutoGenerated { get; set; }
}

public class AgentDetail : AgentSummary
{
    [JsonPropertyName("actions")] public List<AgentAction> Actions { get; set; } = [];
    [JsonPropertyName("guardrails")] public AgentGuardrails Guardrails { get; set; } = new();
    [JsonPropertyName("triggers")] public List<AgentTrigger> Triggers { get; set; } = [];
    [JsonPropertyName("output")] public AgentOutputConfig Output { get; set; } = new();
}

public class ApprovalRequest
{
    [JsonPropertyName("approved")] public bool Approved { get; set; }
    [JsonPropertyName("comment")] public string? Comment { get; set; }
}
