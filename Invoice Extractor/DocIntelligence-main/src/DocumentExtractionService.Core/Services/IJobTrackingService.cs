using DocumentExtractionService.Core.Models;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Abstraction for async job tracking.
/// Separates job state management from the controller so it can
/// be backed by in-memory storage today and swapped to a database,
/// Redis, or Azure Table Storage later without changing consumers.
/// </summary>
public interface IJobTrackingService
{
    /// <summary>Create a new job and return its ID.</summary>
    ExtractionJob CreateJob(int totalFiles, string clientId, string documentType);

    /// <summary>Get a job by ID. Returns null if not found.</summary>
    ExtractionJob? GetJob(string jobId);

    /// <summary>
    /// Thread-safe update of job state.
    /// The <paramref name="update"/> action runs inside the lock.
    /// </summary>
    void UpdateJob(string jobId, Action<ExtractionJob> update);
}
