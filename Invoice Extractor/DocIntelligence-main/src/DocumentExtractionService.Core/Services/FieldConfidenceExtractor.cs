using System.Text.Json;
using DocumentExtractionService.Core.Models;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Extracts field-level confidence scores from the GPT-returned JSON.
///
/// The GPT output includes a "confidence" object at a configurable path
/// (default: root["confidence"]) with numeric scores for critical fields.
/// This service reads those scores and produces a dictionary the UI can consume
/// without digging into the raw Data blob.
/// </summary>
public static class FieldConfidenceExtractor
{
    /// <summary>
    /// Read the confidence object from the extracted data and produce
    /// a field name ? FieldConfidence dictionary.
    /// </summary>
    /// <param name="extractedData">The full GPT extraction result.</param>
    /// <param name="confidencePath">
    /// JSON property name where the confidence object lives (e.g., "confidence").
    /// </param>
    /// <param name="dualPassTriggered">Whether dual-pass verification was used.</param>
    /// <returns>
    /// Dictionary keyed by field name (e.g., "vendor_name", "invoice_number").
    /// Returns null if no confidence data is found — this keeps the response
    /// compact for document types that don't report confidence.
    /// </returns>
    public static Dictionary<string, FieldConfidence>? Extract(
        JsonDocument? extractedData,
        string? confidencePath,
        bool dualPassTriggered)
    {
        if (extractedData is null || string.IsNullOrEmpty(confidencePath))
            return null;

        try
        {
            var root = extractedData.RootElement;

            if (!root.TryGetProperty(confidencePath, out var confidenceElement))
                return null;

            if (confidenceElement.ValueKind != JsonValueKind.Object)
                return null;

            var result = new Dictionary<string, FieldConfidence>(StringComparer.OrdinalIgnoreCase);
            var sourcePass = dualPassTriggered ? "dual_pass" : "primary";

            foreach (var property in confidenceElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number)
                    continue;

                result[property.Name] = new FieldConfidence
                {
                    Score = Math.Clamp(property.Value.GetDecimal(), 0.0m, 1.0m),
                    SourcePass = sourcePass
                };
            }

            return result.Count > 0 ? result : null;
        }
        catch (ObjectDisposedException)
        {
            // extractedData was disposed before we could read it
            return null;
        }
        catch (InvalidOperationException)
        {
            // JsonElement in unexpected state
            return null;
        }
    }
}
