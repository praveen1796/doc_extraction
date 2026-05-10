using System.Collections.Concurrent;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;

namespace DocumentExtractionService.Api.Services;

/// <summary>
/// In-memory extraction result store.
/// Caches completed results so export endpoints can look them up by request ID.
/// Auto-evicts oldest entries when the cache exceeds <see cref="MaxEntries"/>.
///
/// To swap for persistent storage later, implement IExtractionResultStore
/// with a database-backed version and register it in DI.
/// </summary>
public class InMemoryExtractionResultStore : IExtractionResultStore
{
    private const int MaxEntries = 500;
    private const int EvictCount = 100;

    private readonly ConcurrentDictionary<string, StampedResult> _results = new();
    private readonly object _evictionLock = new();

    public void Store(ExtractionResponse result)
    {
        _results[result.RequestId] = new StampedResult(result, DateTime.UtcNow);
        EvictIfNeeded();
    }

    public ExtractionResponse? Get(string requestId)
    {
        return _results.TryGetValue(requestId, out var entry) ? entry.Response : null;
    }

    public void MarkApproved(string requestId, bool hasEdits)
    {
        if (_results.TryGetValue(requestId, out var stamped))
        {
            stamped.Response.HasUserEdits = hasEdits;
            stamped.Response.ApprovedAt = DateTime.UtcNow;

        }
    }


    private void EvictIfNeeded()
    {
        if (_results.Count <= MaxEntries) return;

        lock (_evictionLock)
        {
            if (_results.Count <= MaxEntries) return;

            var toRemove = _results
                .OrderBy(kv => kv.Value.StoredAt)
                .Take(EvictCount)
                .Select(kv => kv.Key)
                .ToList();

            foreach (var key in toRemove)
                _results.TryRemove(key, out _);
        }
    }

    private sealed record StampedResult(ExtractionResponse Response, DateTime StoredAt);
}
