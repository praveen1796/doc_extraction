using DocumentExtractionService.Core.Models;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Abstraction for reporting job progress through the extraction pipeline.
///
/// Sits between callers (controller, background service) and storage
/// (IJobTrackingService). Encapsulates all field-mutation logic for
/// stage transitions so callers express intent, not mechanics.
///
/// EXTENSIBILITY FOR STREAMING:
/// To add SSE or SignalR push later, decorate or extend the implementation:
///   1. After each UpdateJob call, push the updated snapshot to a channel/hub.
///   2. Callers don't change — they still call ReportStage/ReportCompletion/ReportFailure.
///   3. The polling endpoint continues to work unchanged.
/// </summary>
public interface IJobProgressReporter
{
    /// <summary>
    /// Report a stage transition with a human-readable message.
    /// Sets Stage, CurrentMessage, and (on first call) Status = Processing.
    /// </summary>
    void ReportStage(string jobId, JobStage stage, string message);

    /// <summary>
    /// Report successful completion (or partial success) of a batch job.
    /// Derives Stage, Status, ProcessedFiles, FailedFiles, CompletedAt,
    /// CurrentMessage, and Results from the batch result.
    /// </summary>
    void ReportCompletion(string jobId, BatchExtractionResponse result);

    /// <summary>
    /// Report an unrecoverable failure.
    /// Sets Stage = Failed, Status = Failed, ErrorMessage, CompletedAt.
    /// When <paramref name="exception"/> is provided, logs it at Error level
    /// with the full stack trace — callers should not log the exception separately.
    /// </summary>
    void ReportFailure(string jobId, string errorMessage, Exception? exception = null);
}
