using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
namespace DocumentExtractionService.Api.Services;


public interface IApproveQueue
{
    void Enqueue(string requestId, object data);
    bool TryDequeue(out (string requestId, object data) item);
}

public sealed class ApproveQueue : IApproveQueue
{
    private readonly ConcurrentQueue<(string, object)> _queue = new();

    public void Enqueue(string requestId, object data)
        => _queue.Enqueue((requestId, data));

    public bool TryDequeue(out (string requestId, object data) item)
        => _queue.TryDequeue(out item);
}
