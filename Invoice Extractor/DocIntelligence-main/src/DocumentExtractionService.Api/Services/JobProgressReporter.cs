using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;
using Microsoft.Extensions.Logging;

namespace DocumentExtractionService.Api.Services;

/// <summary>
/// Default progress reporter backed by IJobTrackingService.
///
/// All stage-transition logic is centralized here — callers (controller,
/// background service) express intent via semantic methods; this class
/// translates intent into the correct ExtractionJob field mutations.
///
/// TO ADD SSE / SIGNALR LATER:
///   Option A — Decorator:
///     Create SseJobProgressReporter that wraps this class,
///     calls base, then pushes the job snapshot to a channel.
///   Option B — Events:
///     Add an event/callback here (e.g., Action&lt;string, ExtractionJob&gt; OnProgress)
///     that a hub can subscribe to.
///   Either way, no changes to callers or IJobTrackingService.
/// </summary>
public class JobProgressReporter : IJobProgressReporter
{
    private readonly IJobTrackingService _jobTracking;
    private readonly ILogger<JobProgressReporter> _logger;

    public JobProgressReporter(
        IJobTrackingService jobTracking,
        ILogger<JobProgressReporter> logger)
    {
        _jobTracking = jobTracking;
        _logger = logger;
    }

    public void ReportStage(string jobId, JobStage stage, string message)
    {
        _jobTracking.UpdateJob(jobId, j =>
        {
            j.Status = ExtractionStatus.Processing;
            j.Stage = stage;
            j.CurrentMessage = message;
        });

        _logger.LogDebug("[Job:{JobId}] Stage ? {Stage}: {Message}", jobId, stage, message);
    }

    public void ReportCompletion(string jobId, BatchExtractionResponse result)
    {
        _jobTracking.UpdateJob(jobId, j =>
        {
            var allFailed = result.Failed > 0 && result.Succeeded == 0;

            j.Status = result.Status;
            j.Stage = allFailed ? JobStage.Failed : JobStage.Completed;
            j.ProcessedFiles = result.Succeeded + result.Failed;
            j.FailedFiles = result.Failed;
            j.Results = result.Results;
            j.CompletedAt = DateTime.UtcNow;
            j.CurrentMessage = allFailed
                ? $"Failed: {result.Failed} of {j.TotalFiles} documents failed"
                : $"Completed: {result.Succeeded} succeeded, {result.Failed} failed";
        });

        _logger.LogInformation("[Job:{JobId}] Completed: {Succeeded}/{Total} succeeded",
            jobId, result.Succeeded, result.Total);
    }

    public void ReportFailure(string jobId, string errorMessage, Exception? exception = null)
    {
        _jobTracking.UpdateJob(jobId, j =>
        {
            j.Status = ExtractionStatus.Failed;
            j.Stage = JobStage.Failed;
            j.Error = new ErrorDetail
            {
                Code = "job_failed",
                Message = errorMessage
            };
            j.CompletedAt = DateTime.UtcNow;
            j.CurrentMessage = "Job failed with an unexpected error";
        });

        if (exception is not null)
            _logger.LogError(exception, "[Job:{JobId}] Failed: {Error}", jobId, errorMessage);
        else
            _logger.LogWarning("[Job:{JobId}] Failed: {Error}", jobId, errorMessage);
    }
}
