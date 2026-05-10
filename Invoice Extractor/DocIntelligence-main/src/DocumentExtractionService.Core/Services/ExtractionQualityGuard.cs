using System.Text.Json;
using DocumentExtractionService.Core.Models;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Heuristics that flag likely incomplete extractions (especially long contracts where
/// line items only reference very early pages, e.g., max page_ref=11 for a 90-page file).
/// These checks do not replace schema validation; they add user-visible warnings.
/// </summary>
public static class ExtractionQualityGuard
{
    public static bool ApplyCoverageChecks(
        JsonElement root,
        ValidationSummary validation,
        int pageCount,
        string? documentType)
    {
        if (!IsContractLike(documentType) || pageCount < 15) return false;

        if (!root.TryGetProperty("line_items", out var items) || items.ValueKind != JsonValueKind.Array)
            return false;

        int lineItemCount = items.GetArrayLength();
        if (lineItemCount == 0)
        {
            AddWarning(validation,
                "No contract line items were extracted.",
                "coverage_no_line_items",
                "line_items");
            return true;
        }

        int parseableRefs = 0;
        int maxPageRef = 0;
        var uniquePages = new HashSet<int>();

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("page_ref", out var pageRefEl)) continue;
            if (!TryParsePageRef(pageRefEl, out var pageRef)) continue;
            parseableRefs++;
            if (pageRef > maxPageRef) maxPageRef = pageRef;
            uniquePages.Add(pageRef);
        }

        if (parseableRefs == 0)
        {
            AddWarning(validation,
                "Line items were extracted but page_ref values are missing or invalid; cannot verify document coverage.",
                "coverage_page_ref_missing",
                "line_items[*].page_ref");
            return true;
        }

        var coverageRatio = (double)maxPageRef / Math.Max(1, pageCount);
        bool criticalCoverageRisk =
            (pageCount >= 20 && maxPageRef <= 11) ||
            (pageCount >= 20 && coverageRatio < 0.55);

        if (criticalCoverageRisk)
        {
            AddWarning(validation,
                $"Potentially incomplete extraction: highest page_ref is {maxPageRef} for a {pageCount}-page contract. Review later pages or rerun with tighter chunking.",
                "coverage_page_ref_low",
                "line_items[*].page_ref");

            validation.ConfidenceScore = Math.Min(validation.ConfidenceScore, 0.65m);
            return true;
        }

        if (pageCount >= 30 && uniquePages.Count <= 3 && lineItemCount >= 20)
        {
            AddWarning(validation,
                $"Most extracted line items come from only {uniquePages.Count} page(s). Verify that fees/rates from later pages were captured.",
                "coverage_page_ref_concentrated",
                "line_items[*].page_ref");
            validation.ConfidenceScore = Math.Min(validation.ConfidenceScore, 0.80m);
        }

        return false;
    }

    private static bool IsContractLike(string? documentType)
    {
        var dt = (documentType ?? "").Trim().ToLowerInvariant();
        return dt == "contract" || dt == "contracts";
    }

    private static void AddWarning(ValidationSummary validation, string message, string code, string? field)
    {
        if (!validation.Warnings.Contains(message))
            validation.Warnings.Add(message);

        validation.Messages ??= [];
        if (!validation.Messages.Any(m => m.Code == code && m.Message == message))
        {
            validation.Messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Warning,
                Code = code,
                Field = field,
                Message = message,
                SuggestedAction = "Review extracted line_items against late pages in the PDF and rerun extraction if needed."
            });
        }
    }

    private static bool TryParsePageRef(JsonElement value, out int page)
    {
        page = 0;
        if (value.ValueKind == JsonValueKind.Number)
            return value.TryGetInt32(out page) && page > 0;

        if (value.ValueKind != JsonValueKind.String)
            return false;

        var s = value.GetString();
        if (string.IsNullOrWhiteSpace(s)) return false;
        s = s.Trim();

        // Find first contiguous numeric segment.
        int start = -1;
        int len = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsDigit(s[i]))
            {
                if (start < 0) start = i;
                len++;
            }
            else if (start >= 0)
            {
                break;
            }
        }

        if (start < 0 || len <= 0) return false;
        return int.TryParse(s.Substring(start, len), out page) && page > 0;
    }
}
