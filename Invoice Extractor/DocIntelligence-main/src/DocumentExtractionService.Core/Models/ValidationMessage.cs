using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

/// <summary>
/// Structured validation message for the enterprise UI.
/// Each message maps to a specific field and rule, providing enough
/// context for the frontend to render inline warnings, tooltips,
/// and suggested corrective actions.
/// </summary>
public class ValidationMessage
{
    /// <summary>
    /// Severity level: Error, Warning, or Info.
    /// Reuses the existing ValidationSeverity enum from the rule definitions.
    /// </summary>
    [JsonPropertyName("severity")]
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Warning;

    /// <summary>
    /// Stable code for programmatic handling by the frontend.
    /// Derived from the rule ID when available, otherwise auto-generated
    /// from the rule type (e.g., "required", "date_format", "range").
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    /// <summary>
    /// JSON field path that failed validation (e.g., "vendor_name", "invoice_date").
    /// Null for structural validations that don't target a specific field.
    /// </summary>
    [JsonPropertyName("field")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Field { get; set; }

    /// <summary>
    /// Human-readable validation message.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>
    /// Optional action hint for the UI (e.g., "Verify this field exists in the document").
    /// Null when no specific action can be suggested.
    /// </summary>
    [JsonPropertyName("suggested_action")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SuggestedAction { get; set; }

    /// <summary>
    /// 1-based page number where the field was found or expected.
    /// Null when page-level source tracking is not available.
    /// Reserved for future page-preview integration.
    /// </summary>
    [JsonPropertyName("source_page")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SourcePage { get; set; }
}
