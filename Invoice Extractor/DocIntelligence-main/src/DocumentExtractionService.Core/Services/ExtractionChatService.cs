using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocumentExtractionService.Core.Services;

public interface IExtractionChatService
{
    Task<ExtractionChatResponse> ChatAsync(ExtractionChatRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// MVP: Q+A over extracted JSON. Uses in-memory store when <see cref="ExtractionChatRequest.RequestId"/> is set,
/// otherwise accepts inline <see cref="ExtractionChatRequest.Data"/> for client-held results (demo mode).
/// </summary>
public class ExtractionChatService : IExtractionChatService
{
    public const int MaxExtractionJsonChars = 200_000;

    private readonly IExtractionResultStore _resultStore;
    private readonly IGenericOpenAIService _openAI;
    private readonly ILogger<ExtractionChatService> _logger;

    public ExtractionChatService(
        IExtractionResultStore resultStore,
        IGenericOpenAIService openAI,
        ILogger<ExtractionChatService> logger)
    {
        _resultStore = resultStore;
        _openAI = openAI;
        _logger = logger;
    }

    public async Task<ExtractionChatResponse> ChatAsync(ExtractionChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("message is required", nameof(request));

        string extractionJson;
        string documentType;
        string sourceFile;

        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            var stored = _resultStore.Get(request.RequestId.Trim());
            if (stored != null)
            {
                extractionJson = stored.Data?.RootElement.GetRawText() ?? "{}";
                documentType = stored.DocumentType;
                sourceFile = stored.Metadata.SourceFile;
                _logger.LogInformation("Chat using stored extraction request_id={Id} type={Type}", request.RequestId, documentType);
            }
            else if (request.Data.HasValue)
            {
                (extractionJson, documentType, sourceFile) = FromInline(request);
                _logger.LogInformation("Chat using inline data (request_id not in store) id={Id}", request.RequestId);
            }
            else
            {
                throw new InvalidOperationException(
                    $"No extraction found for request_id '{request.RequestId}'. Extract the document first, or send 'data' with the extraction payload.");
            }
        }
        else if (request.Data.HasValue)
        {
            (extractionJson, documentType, sourceFile) = FromInline(request);
            _logger.LogInformation("Chat using inline extraction JSON only");
        }
        else
        {
            throw new ArgumentException("Provide request_id (after server extraction) or data (inline extraction JSON).");
        }

        if (extractionJson.Length > MaxExtractionJsonChars)
        {
            _logger.LogWarning("Extraction JSON truncated for chat from {Len} to {Max} chars",
                extractionJson.Length, MaxExtractionJsonChars);
            extractionJson = extractionJson[..MaxExtractionJsonChars] + "\n/* ... truncated for chat context ... */";
        }

        var reply = await _openAI.ChatOverExtractedJsonAsync(
            documentType,
            sourceFile,
            extractionJson,
            request.Message.Trim(),
            cancellationToken);

        return new ExtractionChatResponse { Reply = reply };
    }

    private static (string Json, string DocType, string SourceFile) FromInline(ExtractionChatRequest request)
    {
        var json = request.Data!.Value.GetRawText();
        var docType = string.IsNullOrWhiteSpace(request.DocumentType) ? "unknown" : request.DocumentType.Trim();
        var file = string.IsNullOrWhiteSpace(request.SourceFile) ? "document" : request.SourceFile.Trim();
        return (json, docType, file);
    }
}
