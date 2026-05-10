using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

// ══════════════════════════════════════════════════════════════════════
//  REQUEST MODELS
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Single document extraction request. Received as multipart/form-data.
/// </summary>
public class ExtractionRequest
{
    /// <summary>
    /// Document type key (e.g., "invoice", "purchase_order", "timesheet").
    /// Must match a folder name under DocumentTypes/.
    /// </summary>
    public string DocumentType { get; set; } = "invoice";

    /// <summary>
    /// Optional extraction options that override document-type defaults.
    /// </summary>
    public ExtractionOptions? Options { get; set; }
}

public class ExtractionOptions
{
    /// <summary>Override the reasoning effort for this request.</summary>
    public string? ReasoningEffort { get; set; }

    /// <summary>Override dual-pass setting for this request.</summary>
    public bool? EnableDualPass { get; set; }

    /// <summary>Override max pages for vision.</summary>
    public int? MaxPagesForVision { get; set; }

    /// <summary>Override max characters of extracted PDF text (null = document type default).</summary>
    public int? MaxTextChars { get; set; }

    /// <summary>Override text truncation: <c>head_only</c> or <c>head_and_tail</c> (null = document type default).</summary>
    public string? TextTruncation { get; set; }

    /// <summary>Additional context to inject into the extraction prompt.</summary>
    public string? AdditionalContext { get; set; }

    /// <summary>Output format hint: "json" (default) or "raw".</summary>
    public string OutputFormat { get; set; } = "json";
}

/// <summary>
/// Batch extraction request - multiple documents of potentially different types.
/// </summary>
public class BatchExtractionRequest
{
    /// <summary>Default document type if not specified per item.</summary>
    public string DefaultDocumentType { get; set; } = "invoice";

    /// <summary>Per-file type overrides: filename → documentType.</summary>
    public Dictionary<string, string> FileTypeOverrides { get; set; } = new();

    /// <summary>Options applied to all files unless overridden.</summary>
    public ExtractionOptions? Options { get; set; }

    /// <summary>
    /// If true, returns a job ID for async polling instead of waiting.
    /// Recommended for batches > 5 files.
    /// </summary>
    public bool Async { get; set; } = false;
}

// ══════════════════════════════════════════════════════════════════════
//  RESPONSE MODELS
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Complete extraction response returned to the caller.
/// </summary>
public class ExtractionResponse
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("document_type")]
    public string DocumentType { get; set; } = "";

    [JsonPropertyName("status")]
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Success;

    [JsonPropertyName("metadata")]
    public ExtractionMetadata Metadata { get; set; } = new();

    [JsonPropertyName("data")]
    public JsonDocument? Data { get; set; }

    [JsonPropertyName("validation")]
    public ValidationSummary Validation { get; set; } = new();

    public bool HasUserEdits { get; set; }
    public DateTime? ApprovedAt { get; set; }


    /// <summary>
    /// Per-field confidence scores extracted from the model output.
    /// Keyed by field name (e.g., "vendor_name", "invoice_number").
    /// Null when the document type does not report confidence.
    /// </summary>
    [JsonPropertyName("field_confidences")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, FieldConfidence>? FieldConfidences { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorDetail? Error { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExtractionStatus
{
    Success,
    PartialSuccess,
    Failed,
    Queued,
    Processing
}

public class ExtractionMetadata
{
    [JsonPropertyName("source_file")]
    public string SourceFile { get; set; } = "";

    [JsonPropertyName("file_size_bytes")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("page_count")]
    public int PageCount { get; set; }

    [JsonPropertyName("extraction_method")]
    public string ExtractionMethod { get; set; } = "";

    [JsonPropertyName("model_used")]
    public string ModelUsed { get; set; } = "";

    [JsonPropertyName("reasoning_effort")]
    public string ReasoningEffort { get; set; } = "";

    [JsonPropertyName("dual_pass_triggered")]
    public bool DualPassTriggered { get; set; }

    [JsonPropertyName("extracted_at")]
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("processing_time_ms")]
    public long ProcessingTimeMs { get; set; }

    [JsonPropertyName("total_tokens_used")]
    public int TotalTokensUsed { get; set; }

    [JsonPropertyName("pages_sent_to_model")]
    public int PagesSentToModel { get; set; }
}

public class ValidationSummary
{
    [JsonPropertyName("is_valid")]
    public bool IsValid { get; set; } = true;

    [JsonPropertyName("confidence_score")]
    public decimal ConfidenceScore { get; set; } = 1.0m;

    /// <summary>Flat warning messages (kept for backward compatibility).</summary>
    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    /// <summary>Flat error messages (kept for backward compatibility).</summary>
    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];

    /// <summary>Computed warning count for quick UI badge rendering.</summary>
    [JsonPropertyName("warning_count")]
    public int WarningCount => Warnings.Count;

    /// <summary>Computed error count for quick UI badge rendering.</summary>
    [JsonPropertyName("error_count")]
    public int ErrorCount => Errors.Count;

    /// <summary>
    /// Structured validation messages with field, severity, code, and suggested action.
    /// Null when no validation rules were evaluated (keeps response compact).
    /// </summary>
    [JsonPropertyName("messages")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ValidationMessage>? Messages { get; set; }
}

public class ErrorDetail
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("details")]
    public string? Details { get; set; }
}

// ══════════════════════════════════════════════════════════════════════
//  BATCH RESPONSE
// ══════════════════════════════════════════════════════════════════════

public class BatchExtractionResponse
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("status")]
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Success;

    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("succeeded")]
    public int Succeeded { get; set; }

    [JsonPropertyName("failed")]
    public int Failed { get; set; }

    [JsonPropertyName("total_processing_time_ms")]
    public long TotalProcessingTimeMs { get; set; }

    [JsonPropertyName("results")]
    public List<ExtractionResponse> Results { get; set; } = [];

    /// <summary>For async batches — poll /api/jobs/{JobId}/status</summary>
    [JsonPropertyName("job_id")]
    public string? JobId { get; set; }

    [JsonPropertyName("poll_url")]
    public string? PollUrl { get; set; }
}

// ══════════════════════════════════════════════════════════════════════
//  JOB TRACKING (ASYNC BATCH)
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Async extraction job state.
/// Enriched with stage, progress, timing, and file metadata
/// so the UI can render meaningful progress indicators.
/// </summary>
public class ExtractionJob
{
    // ── Identity ──────────────────────────────────────────────────────
    [JsonPropertyName("job_id")]
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("client_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientId { get; set; }

    // ── Status & Stage ────────────────────────────────────────────────
    [JsonPropertyName("status")]
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Queued;

    [JsonPropertyName("stage")]
    public JobStage Stage { get; set; } = JobStage.Uploaded;

    [JsonPropertyName("current_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CurrentMessage { get; set; }

    // ── Progress ──────────────────────────────────────────────────────
    [JsonPropertyName("total_files")]
    public int TotalFiles { get; set; }

    [JsonPropertyName("processed_files")]
    public int ProcessedFiles { get; set; }

    [JsonPropertyName("failed_files")]
    public int FailedFiles { get; set; }

    /// <summary>Overall progress 0–100. Computed from processed/total files.</summary>
    [JsonPropertyName("progress_percent")]
    public int ProgressPercent => TotalFiles > 0
        ? Math.Clamp((int)((ProcessedFiles + FailedFiles) * 100L / TotalFiles), 0, 100)
        : 0;

    // ── File / Document Info ──────────────────────────────────────────
    [JsonPropertyName("document_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DocumentType { get; set; }

    /// <summary>True when results are available for retrieval.</summary>
    [JsonPropertyName("result_available")]
    public bool ResultAvailable => Status is ExtractionStatus.Success
        or ExtractionStatus.PartialSuccess;

    // ── Timing ────────────────────────────────────────────────────────
    [JsonPropertyName("started_at_utc")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updated_at_utc")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("completed_at_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? CompletedAt { get; set; }

    /// <summary>Elapsed time since job creation in milliseconds.</summary>
    [JsonPropertyName("elapsed_ms")]
    public long ElapsedMs => (long)(
        (CompletedAt ?? DateTime.UtcNow) - CreatedAt).TotalMilliseconds;

    // ── Error ─────────────────────────────────────────────────────────
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ErrorDetail? Error { get; set; }

    // ── Results (populated on completion) ──────────────────────────────
    [JsonPropertyName("results")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ExtractionResponse>? Results { get; set; }
}

// ══════════════════════════════════════════════════════════════════════
//  INTERNAL PIPELINE MODELS
// ══════════════════════════════════════════════════════════════════════

/// <summary>
/// Processed PDF content ready for GPT. Internal pipeline model.
/// </summary>
public class PdfContent
{
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public long FileSize { get; set; }
    public int PageCount { get; set; }
    public bool IsScanned { get; set; }
    public string ExtractedText { get; set; } = "";
    public List<byte[]> PageImages { get; set; } = [];
    public string ExtractionMethod { get; set; } = "";
}

/// <summary>
/// Result from the OpenAI call. Internal pipeline model.
/// </summary>
public class OpenAiExtractionResult
{
    public string JsonResult { get; set; } = "";
    public int TokensUsed { get; set; }
    public long ElapsedMs { get; set; }
    public bool DualPassTriggered { get; set; }
}
