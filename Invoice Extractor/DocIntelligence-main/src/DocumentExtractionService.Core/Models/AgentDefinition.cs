using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

/// <summary>
/// Agent Definition — the declarative configuration for an autonomous document agent.
///
/// An AgentDefinition wraps a DocumentTypeConfig and adds:
///   - Actions: what the agent can DO beyond extraction (summarize, compare, export, trigger)
///   - Triggers: conditions that start the agent automatically
///   - Guardrails: constraints on the agent's behavior (max steps, cost limits, approval gates)
///   - Personality: how the agent communicates results
///
/// RELATIONSHIP TO DOCUMENT TYPES:
/// Every DocumentType is implicitly an agent with a single "extract" action.
/// Promoting a DocumentType to an explicit AgentDefinition unlocks multi-step workflows.
///
/// LOADED FROM: Agents/{agentId}/agent.json
///   (or synthesized from DocumentTypes/{typeId}/config.json with default agent wrapping)
///
/// NO CODE CHANGES REQUIRED to add a new agent — drop a folder with agent.json + prompts.
/// </summary>
public class AgentDefinition
{
    // ── Identity ──────────────────────────────────────────────────────────
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("icon_name")]
    public string? IconName { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    // ── Source Document Type ───────────────────────────────────────────────
    /// <summary>
    /// The document type this agent operates on.
    /// If set, the agent inherits prompts, schema, and validation from the document type.
    /// </summary>
    [JsonPropertyName("document_type_id")]
    public string? DocumentTypeId { get; set; }

    // ── Actions ───────────────────────────────────────────────────────────
    /// <summary>
    /// The actions this agent can perform.
    /// Each action maps to a registered handler in the ActionRegistry.
    /// Order matters for the default workflow sequence.
    /// </summary>
    [JsonPropertyName("actions")]
    public List<AgentAction> Actions { get; set; } = [];

    // ── Agent Prompts (override or augment document type prompts) ──────────
    /// <summary>
    /// Agent-level system prompt. If set, replaces the document type's system prompt.
    /// Use {{doc_type_system_prompt}} to include the original.
    /// </summary>
    [JsonPropertyName("agent_system_prompt")]
    public string? AgentSystemPrompt { get; set; }

    /// <summary>
    /// Reasoning prompt — used by the AgentRunService's reason step to decide next action.
    /// </summary>
    [JsonPropertyName("reasoning_prompt")]
    public string? ReasoningPrompt { get; set; }

    // ── Guardrails ────────────────────────────────────────────────────────
    [JsonPropertyName("guardrails")]
    public AgentGuardrails Guardrails { get; set; } = new();

    // ── Triggers ──────────────────────────────────────────────────────────
    [JsonPropertyName("triggers")]
    public List<AgentTrigger> Triggers { get; set; } = [];

    // ── Output Configuration ──────────────────────────────────────────────
    [JsonPropertyName("output")]
    public AgentOutputConfig Output { get; set; } = new();

    // ── Runtime (not serialized) ──────────────────────────────────────────
    [JsonIgnore]
    public DocumentTypeConfig? DocumentType { get; set; }

    [JsonIgnore]
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// Create a default AgentDefinition wrapping a DocumentTypeConfig.
    /// This is how existing document types become agents without explicit agent.json.
    /// </summary>
    public static AgentDefinition FromDocumentType(DocumentTypeConfig docType)
    {
        return new AgentDefinition
        {
            Id = $"doc_{docType.Id}",
            DisplayName = docType.DisplayName,
            Description = docType.Description,
            Version = docType.Version,
            Enabled = docType.Enabled,
            IconName = docType.IconName,
            Category = docType.Category,
            DocumentTypeId = docType.Id,
            DocumentType = docType,
            FolderPath = docType.FolderPath,
            Actions =
            [
                new AgentAction
                {
                    Id = "extract",
                    Type = ActionType.Extract,
                    DisplayName = "Extract Data",
                    Description = $"Extract structured data from {docType.DisplayName}",
                    Enabled = true,
                    IsDefault = true,
                },
                new AgentAction
                {
                    Id = "validate",
                    Type = ActionType.Validate,
                    DisplayName = "Validate",
                    Description = "Run validation rules against extracted data",
                    Enabled = true,
                },
                new AgentAction
                {
                    Id = "export",
                    Type = ActionType.Export,
                    DisplayName = "Export",
                    Description = "Export to JSON or Excel",
                    Enabled = docType.Output.ExcelExportEnabled,
                },
            ],
            Tags = ["auto-generated", "document-type"],
        };
    }
}

/// <summary>
/// A discrete action an agent can perform within a run.
/// </summary>
public class AgentAction
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionType Type { get; set; } = ActionType.Custom;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Is this the default/first action in the workflow?</summary>
    [JsonPropertyName("is_default")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// Action-specific prompt (for LLM-based actions like summarize, compare).
    /// </summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>
    /// Output schema for this action (overrides agent-level schema for this step).
    /// </summary>
    [JsonPropertyName("output_schema")]
    public string? OutputSchema { get; set; }

    /// <summary>
    /// Trust level required: "auto" (no approval), "notify", "approve" (human in loop).
    /// </summary>
    [JsonPropertyName("trust_level")]
    public string TrustLevel { get; set; } = "auto";

    /// <summary>
    /// Configuration specific to this action type.
    /// E.g., for "webhook": { "url": "...", "method": "POST" }
    /// </summary>
    [JsonPropertyName("config")]
    public Dictionary<string, object>? Config { get; set; }
}

/// <summary>Builtin action types. "Custom" for user-defined actions.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActionType
{
    Extract,
    Validate,
    Summarize,
    Compare,
    CrossReference,
    Export,
    Notify,
    Webhook,
    TriggerWorkflow,
    Custom
}

/// <summary>
/// Guardrails constrain the agent's autonomy.
/// These are the "ActionTrustGate" loop-awareness controls.
/// </summary>
public class AgentGuardrails
{
    /// <summary>Max reasoning-action-observe loops before forced stop.</summary>
    [JsonPropertyName("max_steps")]
    public int MaxSteps { get; set; } = 10;

    /// <summary>Max total LLM tokens across all steps before forced stop.</summary>
    [JsonPropertyName("max_total_tokens")]
    public int MaxTotalTokens { get; set; } = 50000;

    /// <summary>Max wall-clock time in seconds.</summary>
    [JsonPropertyName("max_duration_seconds")]
    public int MaxDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Actions that require human approval before execution.
    /// Maps action ID → approval mode: "always", "first_time", "on_cost_threshold".
    /// </summary>
    [JsonPropertyName("approval_required")]
    public Dictionary<string, string> ApprovalRequired { get; set; } = new();

    /// <summary>
    /// Action types that this agent is forbidden from performing,
    /// even if configured. Safety boundary.
    /// </summary>
    [JsonPropertyName("blocked_actions")]
    public List<string> BlockedActions { get; set; } = [];

    /// <summary>
    /// Whether the agent can self-modify its action plan during a run.
    /// If false, the initial action sequence is fixed.
    /// </summary>
    [JsonPropertyName("allow_dynamic_planning")]
    public bool AllowDynamicPlanning { get; set; } = false;
}

/// <summary>
/// Trigger conditions that can start an agent run automatically.
/// </summary>
public class AgentTrigger
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "manual"; // "manual", "schedule", "event", "webhook"

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Cron expression for "schedule" triggers.</summary>
    [JsonPropertyName("schedule")]
    public string? Schedule { get; set; }

    /// <summary>Event name/pattern for "event" triggers.</summary>
    [JsonPropertyName("event_pattern")]
    public string? EventPattern { get; set; }

    [JsonPropertyName("config")]
    public Dictionary<string, object>? Config { get; set; }
}

/// <summary>
/// How the agent delivers its results.
/// </summary>
public class AgentOutputConfig
{
    /// <summary>Default output format: "json", "markdown", "excel", "html".</summary>
    [JsonPropertyName("default_format")]
    public string DefaultFormat { get; set; } = "json";

    /// <summary>Whether to include step-by-step reasoning trace in output.</summary>
    [JsonPropertyName("include_reasoning_trace")]
    public bool IncludeReasoningTrace { get; set; } = false;

    /// <summary>Whether to include the intermediate results of each action.</summary>
    [JsonPropertyName("include_intermediate_results")]
    public bool IncludeIntermediateResults { get; set; } = false;

    /// <summary>Notification channels for completed runs.</summary>
    [JsonPropertyName("notify_channels")]
    public List<string> NotifyChannels { get; set; } = [];
}
