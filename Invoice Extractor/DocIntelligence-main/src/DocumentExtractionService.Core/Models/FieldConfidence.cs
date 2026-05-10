using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

/// <summary>
/// Per-field extraction metadata surfaced for the UI.
/// Provides confidence score, source pass, and a classification tier
/// so the frontend can color-code fields, show tooltips, and flag issues.
///
/// Document-type agnostic — works for any type whose schema includes
/// a confidence object (invoices, POs, timesheets, tour sheets, etc.).
/// </summary>
public class FieldConfidence
{
    // ?? Tier thresholds (UI classification boundaries) ??
    private const decimal HighThreshold = 0.85m;
    private const decimal MediumThreshold = 0.60m;

    /// <summary>
    /// Model-reported confidence score for this field (0.0–1.0).
    /// 1.0 = clearly visible, unambiguous.
    /// 0.0 = could not find, returned empty/null.
    /// </summary>
    [JsonPropertyName("score")]
    public decimal Score { get; set; }

    /// <summary>
    /// Which extraction pass produced the final value.
    /// "primary" = first-pass extraction.
    /// "dual_pass" = corrected by second-pass verification.
    /// </summary>
    [JsonPropertyName("source_pass")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourcePass { get; set; }

    /// <summary>
    /// Confidence tier for quick UI classification.
    /// "high" (? 0.85), "medium" (? 0.60), "low" (&lt; 0.60).
    /// </summary>
    [JsonPropertyName("tier")]
    public string Tier => Score switch
    {
        >= HighThreshold => "high",
        >= MediumThreshold => "medium",
        _ => "low"
    };
}
