using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

/// <summary>
/// Configuration for a document type. Loaded from DocumentTypes/{typeId}/config.json.
///
/// ADDING A NEW DOCUMENT TYPE:
/// 1. Create folder: DocumentTypes/{new_type}/
/// 2. Add config.json  → metadata + settings
/// 3. Add system_prompt.txt  → role + rules for GPT
/// 4. Add extraction_prompt.txt  → field-by-field extraction instructions
/// 5. Add schema.json  → JSON schema for structured output
/// 6. (Optional) Add validation_rules.json → custom validation logic
/// 7. Restart the service (or if hot-reload enabled, it picks up automatically)
///
/// NO CODE CHANGES REQUIRED.
/// </summary>
public class DocumentTypeConfig
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

    /// <summary>
    /// File extensions this type accepts.
    /// Default: [".pdf"]
    /// Add ".png", ".jpg", ".tiff" etc. for image-only documents.
    /// </summary>
    [JsonPropertyName("accepted_extensions")]
    public List<string> AcceptedExtensions { get; set; } = [".pdf"];

    /// <summary>Maximum file size in megabytes. 0 = use global default.</summary>
    [JsonPropertyName("max_file_size_mb")]
    public int MaxFileSizeMb { get; set; }

    /// <summary>Maximum number of pages to process. 0 = unlimited.</summary>
    [JsonPropertyName("max_pages")]
    public int MaxPages { get; set; }

    /// <summary>UI icon name (e.g., "receipt", "clipboard", "hard-hat"). Optional.</summary>
    [JsonPropertyName("icon_name")]
    public string? IconName { get; set; }

    /// <summary>Grouping category for the UI (e.g., "Finance", "Operations"). Optional.</summary>
    [JsonPropertyName("category")]
    public string? Category { get; set; }

    // ── Prompt File Paths (relative to the document type folder) ──────────
    [JsonPropertyName("system_prompt_file")]
    public string SystemPromptFile { get; set; } = "system_prompt.txt";

    [JsonPropertyName("extraction_prompt_file")]
    public string ExtractionPromptFile { get; set; } = "extraction_prompt.txt";

    /// <summary>
    /// JSON schema for structured output enforcement.
    /// The schema defines EXACTLY what fields GPT must return.
    /// </summary>
    [JsonPropertyName("schema_file")]
    public string SchemaFile { get; set; } = "schema.json";

    /// <summary>
    /// Optional: custom validation rules file.
    /// If absent, only basic JSON validity is checked.
    /// </summary>
    [JsonPropertyName("validation_rules_file")]
    public string? ValidationRulesFile { get; set; }

    // ── Extraction Settings ────────────────────────────────────────────────
    [JsonPropertyName("extraction_settings")]
    public DocumentExtractionSettings ExtractionSettings { get; set; } = new();

    // ── Dual-Pass Configuration ────────────────────────────────────────────
    [JsonPropertyName("dual_pass")]
    public DualPassConfig DualPass { get; set; } = new();

    /// <summary>Optional chunked / two-phase extraction (see <see cref="ChunkingConfig"/>).</summary>
    [JsonPropertyName("chunking")]
    public ChunkingConfig? Chunking { get; set; }

    // ── Output Configuration ───────────────────────────────────────────────
    [JsonPropertyName("output")]
    public DocumentOutputConfig Output { get; set; } = new();

    // ── Rate Limiting (per document type) ─────────────────────────────────
    /// <summary>
    /// If set, overrides the global rate limit for this document type.
    /// Useful for expensive types (e.g., complex invoices with dual-pass).
    /// </summary>
    [JsonPropertyName("rate_limit_override")]
    public RateLimitOverride? RateLimitOverride { get; set; }

    // ── Loaded at runtime (not from JSON) ─────────────────────────────────
    [JsonIgnore]
    public string SystemPrompt { get; set; } = "";

    [JsonIgnore]
    public string ExtractionPromptTemplate { get; set; } = "";

    /// <summary>Loaded from <c>chunk_extraction_prompt_file</c> when chunking is enabled.</summary>
    [JsonIgnore]
    public string? ChunkExtractionPrompt { get; set; }

    [JsonIgnore]
    public string? MapPrompt { get; set; }

    [JsonIgnore]
    public string? MapSchema { get; set; }

    [JsonIgnore]
    public string? PerWellPrompt { get; set; }

    [JsonIgnore]
    public string? PerWellSchema { get; set; }

    [JsonIgnore]
    public string JsonSchema { get; set; } = "";

    [JsonIgnore]
    public List<ValidationRule> ValidationRules { get; set; } = [];

    [JsonIgnore]
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// Resolved path to the confidence object in extracted JSON.
    /// Falls back to DualPass.ConfidencePath for backward compatibility.
    /// </summary>
    [JsonIgnore]
    public string EffectiveConfidencePath => DualPass.ConfidencePath;

    /// <summary>
    /// Top-level field names from the loaded JSON schema (excluding internal fields).
    /// Used by the UI to preview what fields this document type extracts.
    /// Computed lazily from the loaded schema — never serialized.
    /// </summary>
    [JsonIgnore]
    public List<string> SampleFields => GetSampleFields();

    private List<string>? _sampleFieldsCache;

    private List<string> GetSampleFields()
    {
        if (_sampleFieldsCache is not null) return _sampleFieldsCache;

        if (string.IsNullOrEmpty(JsonSchema))
        {
            _sampleFieldsCache = [];
            return _sampleFieldsCache;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(JsonSchema);
            if (doc.RootElement.TryGetProperty("properties", out var props) &&
                props.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                _sampleFieldsCache = props.EnumerateObject()
                    .Where(p => p.Name != "confidence" &&
                                p.Name != "document_type" &&
                                p.Name != "language")
                    .Select(p => p.Name)
                    .Take(12)
                    .ToList();
            }
            else
            {
                _sampleFieldsCache = [];
            }
        }
        catch
        {
            _sampleFieldsCache = [];
        }

        return _sampleFieldsCache;
    }
}

/// <summary>
/// Configuration for chunked extraction of large multi-section documents.
/// </summary>
public class ChunkingConfig
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("chunk_size_pages")]
    public int ChunkSizePages { get; set; } = 10;

    [JsonPropertyName("page_threshold")]
    public int PageThreshold { get; set; } = 15;

    [JsonPropertyName("max_concurrent_chunks")]
    public int MaxConcurrentChunks { get; set; } = 2;

    [JsonPropertyName("chunk_extraction_prompt_file")]
    public string? ChunkExtractionPromptFile { get; set; }

    /// <summary>
    /// <c>by_section</c> (default): split on exhibit/section boundaries — can produce a very small first chunk.
    /// <c>by_page</c>: fixed windows of <see cref="ChunkSizePages"/> so every part of the PDF gets a dedicated pass (better for service orders and long “Main” bodies without Exhibit lines).
    /// </summary>
    [JsonPropertyName("chunk_plan")]
    public string ChunkPlan { get; set; } = "by_section";

    /// <summary><c>concatenate</c>, <c>deep_merge</c>, or <c>two_phase</c> (well plans).</summary>
    [JsonPropertyName("merge_strategy")]
    public string MergeStrategy { get; set; } = "concatenate";

    [JsonPropertyName("merge_array_field")]
    public string MergeArrayField { get; set; } = "line_items";

    [JsonPropertyName("merge_key_field")]
    public string? MergeKeyField { get; set; }

    [JsonPropertyName("merge_nested_arrays")]
    public List<string> MergeNestedArrays { get; set; } = [];

    [JsonPropertyName("map_prompt_file")]
    public string? MapPromptFile { get; set; }

    [JsonPropertyName("map_schema_file")]
    public string? MapSchemaFile { get; set; }

    [JsonPropertyName("per_item_prompt_file")]
    public string? PerItemPromptFile { get; set; }

    [JsonPropertyName("per_item_schema_file")]
    public string? PerItemSchemaFile { get; set; }
}

public class DocumentExtractionSettings
{
    /// <summary>Maximum pages to send to vision model.</summary>
    [JsonPropertyName("max_pages_for_vision")]
    public int MaxPagesForVision { get; set; } = 12;

    /// <summary>DPI for rendering PDF pages to images.</summary>
    [JsonPropertyName("image_dpi")]
    public int ImageDpi { get; set; } = 200;

    /// <summary>Maximum image width in pixels.</summary>
    [JsonPropertyName("image_max_width_px")]
    public int ImageMaxWidthPx { get; set; } = 2048;

    /// <summary>GPT reasoning effort: "low", "medium", "high".</summary>
    [JsonPropertyName("reasoning_effort")]
    public string ReasoningEffort { get; set; } = "medium";

    /// <summary>Max output tokens for primary extraction.</summary>
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 8192;

    /// <summary>Temperature — always 0 for deterministic extraction.</summary>
    [JsonPropertyName("temperature")]
    public float Temperature { get; set; } = 0.0f;

    /// <summary>
    /// Max characters of PDF text to send to the model (0 = default 120,000 in PDF processor).
    /// Increase for long text-based contracts; very large values plus many page images can exceed model context.
    /// </summary>
    [JsonPropertyName("max_text_chars")]
    public int MaxTextChars { get; set; } = 0;

    /// <summary>
    /// When text exceeds <see cref="MaxTextChars"/>: <c>head_only</c> (default) or <c>head_and_tail</c> to keep
    /// the end of the document (exhibits) in context.
    /// </summary>
    [JsonPropertyName("text_truncation")]
    public string TextTruncation { get; set; } = "head_only";
}

public class DualPassConfig
{
    /// <summary>Enable dual-pass verification for this document type.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Fields to check for dual-pass triggering.
    /// These are JSON path keys in the extracted data.
    /// </summary>
    [JsonPropertyName("critical_fields")]
    public List<string> CriticalFields { get; set; } = [];

    /// <summary>Confidence threshold below which dual-pass triggers.</summary>
    [JsonPropertyName("confidence_threshold")]
    public decimal ConfidenceThreshold { get; set; } = 0.70m;

    /// <summary>
    /// Path to confidence object in the extracted JSON.
    /// E.g., "confidence" → looks for root["confidence"]
    /// </summary>
    [JsonPropertyName("confidence_path")]
    public string ConfidencePath { get; set; } = "confidence";
}

public class DocumentOutputConfig
{
    /// <summary>Include extraction metadata in the response.</summary>
    [JsonPropertyName("include_metadata")]
    public bool IncludeMetadata { get; set; } = true;

    /// <summary>Include raw OCR text in the response.</summary>
    [JsonPropertyName("include_raw_text")]
    public bool IncludeRawText { get; set; } = false;

    /// <summary>Indent output JSON for readability.</summary>
    [JsonPropertyName("indent_json")]
    public bool IndentJson { get; set; } = true;

    /// <summary>Whether Excel export is enabled for this document type.</summary>
    [JsonPropertyName("excel_export_enabled")]
    public bool ExcelExportEnabled { get; set; } = true;
}

public class RateLimitOverride
{
    [JsonPropertyName("requests_per_minute")]
    public int RequestsPerMinute { get; set; } = 10;

    [JsonPropertyName("requests_per_day")]
    public int RequestsPerDay { get; set; } = 200;
}

/// <summary>
/// A single validation rule loaded from validation_rules.json.
/// Enables configuration-driven validation without code changes.
/// </summary>
public class ValidationRule
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("severity")]
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    /// <summary>Type of validation: "required", "format", "range", "regex", "cross_field".</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "required";

    /// <summary>JSON path to the field being validated (e.g., "vendor_name", "line_items[*].amount").</summary>
    [JsonPropertyName("field")]
    public string Field { get; set; } = "";

    /// <summary>Expected format pattern for "format" type (e.g., "YYYY-MM-DD" for dates).</summary>
    [JsonPropertyName("pattern")]
    public string? Pattern { get; set; }

    /// <summary>Human-readable message shown when this rule fails.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>Confidence reduction if this rule fails (0.0–1.0).</summary>
    [JsonPropertyName("confidence_penalty")]
    public decimal ConfidencePenalty { get; set; } = 0.05m;

    /// <summary>Conditions for conditional rules (e.g., only run if document_type == "Invoice").</summary>
    [JsonPropertyName("condition")]
    public Dictionary<string, string>? Condition { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}
