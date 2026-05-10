using System.Text.Json;
using System.Text.RegularExpressions;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Configuration-driven validation service.
/// Rules are defined in validation_rules.json per document type — no code changes needed.
///
/// BUILT-IN RULE TYPES:
/// - "required"    → field must not be null/empty
/// - "format"      → field must match a format pattern
/// - "regex"       → field must match a regex pattern
/// - "date"        → field must be YYYY-MM-DD
/// - "range"       → numeric field must be in range
/// - "cross_field" → relationship between two fields
///
/// ADDING CUSTOM RULES: Add to the document type's validation_rules.json
/// </summary>
public class ConfigurableValidationService
{
    private readonly ILogger<ConfigurableValidationService> _logger;

    // Well-known format patterns
    private static readonly Dictionary<string, string> FormatPatterns = new()
    {
        ["YYYY-MM-DD"] = @"^\d{4}-\d{2}-\d{2}$",
        ["ISO_DATE"] = @"^\d{4}-\d{2}-\d{2}$",
        ["ISO_CURRENCY"] = @"^[A-Z]{3}$",
        ["EMAIL"] = @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        ["PHONE"] = @"^[\d\s\+\-\(\)\.]{7,20}$",
    };

    public ConfigurableValidationService(ILogger<ConfigurableValidationService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validate extracted data against the document type's rules.
    /// </summary>
    public ValidationSummary Validate(
        JsonElement data,
        List<ValidationRule> rules,
        string fileName)
    {
        var summary = new ValidationSummary { IsValid = true };
        decimal confidence = 1.0m;

        if (rules.Count > 0)
        {
            summary.Messages = [];
        }

        foreach (var rule in rules)
        {
            // Check rule condition (e.g., only run if document_type == "Invoice")
            if (!EvaluateCondition(data, rule.Condition)) continue;

            var (passed, message) = EvaluateRule(data, rule);

            if (!passed)
            {
                var msg = !string.IsNullOrEmpty(message) ? message : rule.Message;

                switch (rule.Severity)
                {
                    case ValidationSeverity.Error:
                        summary.Errors.Add(msg);
                        summary.IsValid = false;
                        break;
                    case ValidationSeverity.Warning:
                        summary.Warnings.Add(msg);
                        break;
                    case ValidationSeverity.Info:
                        summary.Warnings.Add($"ℹ️ {msg}");
                        break;
                }

                summary.Messages?.Add(new ValidationMessage
                {
                    Severity = rule.Severity,
                    Code = !string.IsNullOrEmpty(rule.Id) ? rule.Id : rule.Type,
                    Field = !string.IsNullOrEmpty(rule.Field) ? rule.Field : null,
                    Message = msg,
                    SuggestedAction = GetSuggestedAction(rule.Type)
                });

                confidence -= rule.ConfidencePenalty;
            }
        }

        // Apply generic JSON quality check if no rules provided
        if (rules.Count == 0)
        {
            RunBasicChecks(data, summary, ref confidence, fileName);
        }

        summary.ConfidenceScore = Math.Max(0.0m, Math.Min(1.0m, confidence));
        return summary;
    }

    /// <summary>
    /// Map rule type to a user-friendly suggested action for the UI.
    /// </summary>
    private static string? GetSuggestedAction(string ruleType)
    {
        return ruleType.ToLowerInvariant() switch
        {
            "required" => "Verify this field exists in the document and re-extract if needed.",
            "format" or "date" => "Check the format of this field value in the source document.",
            "range" => "Verify the value is within the expected range.",
            "regex" => "Verify the value matches the expected pattern.",
            "cross_field" => "Check the relationship between the related fields.",
            "not_value" => "This value appears incorrect — verify against the source document.",
            _ => null
        };
    }

    private (bool Passed, string Message) EvaluateRule(JsonElement data, ValidationRule rule)
    {
        return rule.Type.ToLower() switch
        {
            "required" => EvaluateRequired(data, rule),
            "format" => EvaluateFormat(data, rule),
            "regex" => EvaluateRegex(data, rule),
            "date" => EvaluateDate(data, rule),
            "range" => EvaluateRange(data, rule),
            "cross_field" => EvaluateCrossField(data, rule),
            "not_value" => EvaluateNotValue(data, rule),
            _ => (true, "")
        };
    }

    private static (bool Passed, string Message) EvaluateRequired(JsonElement data, ValidationRule rule)
    {
        var value = GetFieldValue(data, rule.Field);
        bool isEmpty = value == null ||
                       value.Value.ValueKind == JsonValueKind.Null ||
                       (value.Value.ValueKind == JsonValueKind.String &&
                        string.IsNullOrWhiteSpace(value.Value.GetString()));

        return (!isEmpty, isEmpty ? rule.Message : "");
    }

    private static (bool Passed, string Message) EvaluateFormat(JsonElement data, ValidationRule rule)
    {
        var value = GetFieldValue(data, rule.Field);
        if (value == null || value.Value.ValueKind != JsonValueKind.String) return (true, "");

        var strValue = value.Value.GetString() ?? "";
        if (string.IsNullOrEmpty(strValue)) return (true, "");

        if (string.IsNullOrEmpty(rule.Pattern)) return (true, "");

        // Check well-known format patterns
        string pattern = FormatPatterns.TryGetValue(rule.Pattern, out var p) ? p : rule.Pattern;

        bool matches = Regex.IsMatch(strValue, pattern);
        return (matches, matches ? "" : $"{rule.Message}: '{strValue}' does not match format {rule.Pattern}");
    }

    private static (bool Passed, string Message) EvaluateRegex(JsonElement data, ValidationRule rule)
    {
        var value = GetFieldValue(data, rule.Field);
        if (value == null || value.Value.ValueKind != JsonValueKind.String) return (true, "");

        var strValue = value.Value.GetString() ?? "";
        if (string.IsNullOrEmpty(strValue)) return (true, "");
        if (string.IsNullOrEmpty(rule.Pattern)) return (true, "");

        bool matches = Regex.IsMatch(strValue, rule.Pattern, RegexOptions.IgnoreCase);
        return (matches, matches ? "" : $"{rule.Message}: '{strValue}'");
    }

    private static (bool Passed, string Message) EvaluateDate(JsonElement data, ValidationRule rule)
    {
        var value = GetFieldValue(data, rule.Field);
        if (value == null || value.Value.ValueKind != JsonValueKind.String) return (true, "");

        var dateStr = value.Value.GetString() ?? "";
        if (string.IsNullOrEmpty(dateStr)) return (true, "");

        bool isValidFormat = Regex.IsMatch(dateStr, @"^\d{4}-\d{2}-\d{2}$");
        if (!isValidFormat)
        {
            return (false, $"{rule.Field} must be in YYYY-MM-DD format, got: '{dateStr}'");
        }

        if (DateTime.TryParse(dateStr, out var date))
        {
            if (date.Year < 2000 || date.Year > DateTime.Now.Year + 5)
            {
                return (false, $"{rule.Field} has unusual year: {date.Year}");
            }
        }

        return (true, "");
    }

    private static (bool Passed, string Message) EvaluateRange(JsonElement data, ValidationRule rule)
    {
        var value = GetFieldValue(data, rule.Field);
        if (value == null || value.Value.ValueKind == JsonValueKind.Null) return (true, "");
        if (value.Value.ValueKind != JsonValueKind.Number) return (true, "");

        var numValue = value.Value.GetDecimal();

        if (!string.IsNullOrEmpty(rule.Pattern))
        {
            // Pattern format: "min:max" e.g., "0:100000000"
            var parts = rule.Pattern.Split(':');
            if (parts.Length == 2)
            {
                if (decimal.TryParse(parts[0], out var min) && numValue < min)
                    return (false, $"{rule.Message}: {numValue} is below minimum {min}");
                if (decimal.TryParse(parts[1], out var max) && numValue > max)
                    return (false, $"{rule.Message}: {numValue} exceeds maximum {max}");
            }
        }

        return (true, "");
    }

    private static (bool Passed, string Message) EvaluateCrossField(JsonElement data, ValidationRule rule)
    {
        // Cross-field rules use Condition to specify the comparison
        // E.g., condition: { "field2": "due_date", "operator": "after" }
        if (rule.Condition == null) return (true, "");

        if (!rule.Condition.TryGetValue("field2", out var field2Name)) return (true, "");
        if (!rule.Condition.TryGetValue("operator", out var op)) return (true, "");

        var value1 = GetFieldValue(data, rule.Field);
        var value2 = GetFieldValue(data, field2Name);

        if (value1 == null || value2 == null) return (true, "");
        if (value1.Value.ValueKind != JsonValueKind.String) return (true, "");
        if (value2.Value.ValueKind != JsonValueKind.String) return (true, "");

        var str1 = value1.Value.GetString() ?? "";
        var str2 = value2.Value.GetString() ?? "";

        if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2)) return (true, "");

        if (op == "before" || op == "after")
        {
            if (DateTime.TryParse(str1, out var date1) && DateTime.TryParse(str2, out var date2))
            {
                bool valid = op == "before" ? date1 <= date2 : date1 >= date2;
                return (valid, valid ? "" : rule.Message);
            }
        }

        return (true, "");
    }

    private static (bool Passed, string Message) EvaluateNotValue(JsonElement data, ValidationRule rule)
    {
        var value = GetFieldValue(data, rule.Field);
        if (value == null) return (true, "");

        var strValue = value.Value.ValueKind == JsonValueKind.String
            ? value.Value.GetString() ?? "" : "";

        if (string.IsNullOrEmpty(rule.Pattern)) return (true, "");

        var forbiddenValues = rule.Pattern.Split('|');
        bool hasForbiddenValue = forbiddenValues.Any(v =>
            strValue.Equals(v, StringComparison.OrdinalIgnoreCase));

        return (!hasForbiddenValue, hasForbiddenValue ? rule.Message : "");
    }

    private static bool EvaluateCondition(JsonElement data, Dictionary<string, string>? condition)
    {
        if (condition == null || condition.Count == 0) return true;

        foreach (var (field, expectedValue) in condition)
        {
            // Skip cross-field operators (used in EvaluateCrossField)
            if (field == "field2" || field == "operator") continue;

            var actualValue = GetFieldValue(data, field);
            if (actualValue == null) return false;

            var actualStr = actualValue.Value.ValueKind == JsonValueKind.String
                ? actualValue.Value.GetString() ?? "" : "";

            if (!actualStr.Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Basic sanity checks when no custom rules are defined.
    /// </summary>
    private static void RunBasicChecks(
        JsonElement data, ValidationSummary summary, ref decimal confidence, string fileName)
    {
        // Check for obvious structural issues
        if (data.ValueKind != JsonValueKind.Object)
        {
            var msg = "Extraction result is not a JSON object";
            summary.Errors.Add(msg);
            summary.IsValid = false;
            confidence -= 0.5m;

            summary.Messages ??= [];
            summary.Messages.Add(new ValidationMessage
            {
                Severity = ValidationSeverity.Error,
                Code = "invalid_structure",
                Message = msg,
                SuggestedAction = "The extraction produced an invalid result. Try re-extracting the document."
            });
        }
    }

    private static JsonElement? GetFieldValue(JsonElement root, string fieldPath)
    {
        // Simple dot-notation path support: "confidence.vendor_name"
        var parts = fieldPath.Split('.');
        var current = root;

        foreach (var part in parts)
        {
            if (!current.TryGetProperty(part, out var next))
                return null;
            current = next;
        }

        return current;
    }
}
