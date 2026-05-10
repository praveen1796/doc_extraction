using System.Collections.Concurrent;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;

namespace DocumentExtractionService.Api.Services;

/// <summary>
/// In-memory job tracking service.
/// Thread-safe via ConcurrentDictionary + per-job locking on updates.
/// Keeps the last <see cref="MaxJobs"/> jobs and evicts oldest on overflow.
///
/// To swap for persistent storage later, implement IJobTrackingService
/// with a database-backed version and register it in DI.
/// </summary>
public class InMemoryJobTrackingService : IJobTrackingService
{
    private const int MaxJobs = 200;
    private const int EvictCount = 50;

    private readonly ConcurrentDictionary<string, ExtractionJob> _jobs = new();
    private readonly object _evictionLock = new();

    public ExtractionJob CreateJob(int totalFiles, string clientId, string documentType)
    {
        var job = new ExtractionJob
        {
            ClientId = clientId,
            TotalFiles = totalFiles,
            DocumentType = documentType,
            Status = ExtractionStatus.Queued,
            Stage = JobStage.Uploaded,
            CurrentMessage = "Job queued for processing"
        };

        _jobs[job.JobId] = job;
        EvictIfNeeded();

        return job;
    }

    public ExtractionJob? GetJob(string jobId)
    {
        return _jobs.TryGetValue(jobId, out var job) ? job : null;
    }

    public void UpdateJob(string jobId, Action<ExtractionJob> update)
    {
        if (_jobs.TryGetValue(jobId, out var job))
        {
            lock (job)
            {
                update(job);
                job.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    private void EvictIfNeeded()
    {
        if (_jobs.Count <= MaxJobs) return;

        lock (_evictionLock)
        {
            if (_jobs.Count <= MaxJobs) return;

            var toRemove = _jobs
                .OrderBy(kv => kv.Value.CreatedAt)
                .Take(EvictCount)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in toRemove)
                _jobs.TryRemove(key, out _);
        }
    }
}
