using DocumentExtractionService.Core.Models;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Abstraction for extraction result storage.
/// Export endpoints look up completed results by request ID.
///
/// In-memory implementation today; swap for database/blob storage later.
/// </summary>
public interface IExtractionResultStore
{
    /// <summary>Store a completed extraction result.</summary>
    void Store(ExtractionResponse result);

    /// <summary>Retrieve a result by request ID. Returns null if not found or expired.</summary>
    ExtractionResponse? Get(string requestId);
    void MarkApproved(string requestId, bool hasEdits);
}
