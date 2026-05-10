using System.Diagnostics;
using System.Text.Json;
using DocumentExtractionService.Core.Configuration;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Generic Document Extraction Service.
/// Orchestrates: PDF processing → GPT extraction → dual-pass → validation → response.
///
/// This service is document-type agnostic — it uses the DocumentTypeConfig
/// to determine prompts, schema, and validation rules at runtime.
/// </summary>
public interface IDocumentExtractionService
{
    Task<ExtractionResponse> ExtractAsync(
        Stream documentStream,
        string fileName,
        string documentType,
        ExtractionOptions? options = null,
        string? requestId = null,
        CancellationToken cancellationToken = default);

    Task<BatchExtractionResponse> ExtractBatchAsync(
        IList<(Stream Stream, string FileName, string? DocumentType)> documents,
        ExtractionOptions? options = null,
        CancellationToken cancellationToken = default);
}

public class DocumentExtractionService : IDocumentExtractionService
{
    private readonly IDocumentTypeRegistry _registry;
    private readonly IGenericOpenAIService _openAI;
    private readonly PdfProcessorService _pdfProcessor;
    private readonly ConfigurableValidationService _validationService;
    private readonly IExtractionResultStore _resultStore;
    private readonly ProcessingSettings _processingSettings;
    private readonly ChunkedExtractionStrategy? _chunkedStrategy;
    private readonly ILogger<DocumentExtractionService> _logger;

    public DocumentExtractionService(
        IDocumentTypeRegistry registry,
        IGenericOpenAIService openAI,
        PdfProcessorService pdfProcessor,
        ConfigurableValidationService validationService,
        IExtractionResultStore resultStore,
        IOptions<AppSettings> settings,
        ILogger<DocumentExtractionService> logger,
        ChunkedExtractionStrategy? chunkedStrategy = null)
    {
        _registry = registry;
        _openAI = openAI;
        _pdfProcessor = pdfProcessor;
        _validationService = validationService;
        _resultStore = resultStore;
        _processingSettings = settings.Value.Processing;
        _chunkedStrategy = chunkedStrategy;
        _logger = logger;
    }

    /// <summary>
    /// Extract data from a single document.
    /// </summary>
    public async Task<ExtractionResponse> ExtractAsync(
        Stream documentStream,
        string fileName,
        string documentType,
        ExtractionOptions? options = null,
        string? requestId = null,
        CancellationToken cancellationToken = default)
    {
        requestId ??= Guid.NewGuid().ToString("N");
        var sw = Stopwatch.StartNew();

        _logger.LogInformation("[{RequestId}] Extracting {File} as {DocType}",
            requestId, fileName, documentType);

        // ── 1. Get document type config ──
        var docTypeConfig = _registry.GetDocumentType(documentType);
        if (docTypeConfig == null)
        {
            return ErrorResponse(requestId, documentType, fileName,
                "INVALID_DOCUMENT_TYPE",
                $"Document type '{documentType}' not found or not enabled. " +
                $"Available types: {string.Join(", ", _registry.GetAllDocumentTypes().Select(t => t.Id))}");
        }

        // ── 2. Save stream to temp file ──
        string tempFilePath = "";
        try
        {
            tempFilePath = await SaveToTempFileAsync(documentStream, fileName);
        }
        catch (Exception ex)
        {
            return ErrorResponse(requestId, documentType, fileName,
                "FILE_SAVE_ERROR", ex.Message);
        }

        try
        {
            // Merge request options with document type defaults (used for PDF and GPT)
            var effectiveSettings = MergeSettings(docTypeConfig.ExtractionSettings, options);

            // ── 3. Process PDF (text + images) ──
            PdfContent pdfContent;
            try
            {
                var effectiveMaxText = effectiveSettings.MaxTextChars > 0
                    ? effectiveSettings.MaxTextChars
                    : 0;

                var truncMode = TextTruncationMode.HeadOnly;
                var ts = effectiveSettings.TextTruncation?.Trim().ToLowerInvariant();
                if (ts is "head_and_tail" or "headtail" or "head+tail")
                    truncMode = TextTruncationMode.HeadAndTail;

                pdfContent = _pdfProcessor.ProcessPdf(tempFilePath, new PdfProcessingOptions
                {
                    MaxPagesForVision = effectiveSettings.MaxPagesForVision,
                    ImageDpi = docTypeConfig.ExtractionSettings.ImageDpi,
                    ImageMaxWidthPx = docTypeConfig.ExtractionSettings.ImageMaxWidthPx,
                    MaxTextChars = effectiveMaxText,
                    TextTruncation = truncMode,
                    UseDualTextExtraction = string.Equals(
                        docTypeConfig.Id, "invoice", StringComparison.OrdinalIgnoreCase)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] PDF processing failed for {File}", requestId, fileName);
                return ErrorResponse(requestId, documentType, fileName,
                    "PDF_PROCESSING_ERROR", ex.Message);
            }

            if (_chunkedStrategy != null &&
                docTypeConfig.Chunking?.Enabled == true &&
                pdfContent.PageCount > docTypeConfig.Chunking.PageThreshold)
            {
                _logger.LogInformation(
                    "[{RequestId}] Document has {Pages} pages (threshold: {Threshold}) — using chunked extraction",
                    requestId, pdfContent.PageCount, docTypeConfig.Chunking.PageThreshold);

                return await _chunkedStrategy.ExtractChunkedAsync(
                    tempFilePath,
                    pdfContent,
                    docTypeConfig,
                    effectiveSettings,
                    requestId,
                    cancellationToken);
            }

            // ── 4. Primary extraction via GPT ──
            OpenAiExtractionResult extractionResult;
            try
            {
                extractionResult = await _openAI.ExtractAsync(
                    pdfContent,
                    docTypeConfig.SystemPrompt,
                    docTypeConfig.ExtractionPromptTemplate,
                    docTypeConfig.JsonSchema,
                    effectiveSettings,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[{RequestId}] OpenAI extraction failed for {File}", requestId, fileName);
                return ErrorResponse(requestId, documentType, fileName,
                    "EXTRACTION_ERROR", ex.Message);
            }

            // ── 5. Parse result JSON ──
            JsonDocument? extractedData = null;
            try
            {
                extractedData = JsonDocument.Parse(extractionResult.JsonResult);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "[{RequestId}] JSON parse failed for {File}", requestId, fileName);
                return ErrorResponse(requestId, documentType, fileName,
                    "JSON_PARSE_ERROR",
                    $"GPT returned invalid JSON: {ex.Message}");
            }

            // ── 6. Dual-pass verification ──
            bool dualPassTriggered = false;
            bool shouldRunDualPass = (options?.EnableDualPass ?? docTypeConfig.DualPass.Enabled)
                && docTypeConfig.DualPass.CriticalFields.Count > 0
                && pdfContent.PageImages.Count > 0
                && ShouldTriggerDualPass(extractedData, docTypeConfig);

            if (shouldRunDualPass)
            {
                _logger.LogInformation("[{RequestId}] Triggering dual-pass for {File}", requestId, fileName);
                dualPassTriggered = true;

                var correctedJson = await _openAI.RunDualPassAsync(
                    pdfContent,
                    docTypeConfig.SystemPrompt,
                    docTypeConfig.JsonSchema,
                    extractionResult.JsonResult,
                    docTypeConfig.DualPass.CriticalFields,
                    docTypeConfig.DualPass.ConfidenceThreshold,
                    effectiveSettings,
                    cancellationToken);

                if (!string.IsNullOrWhiteSpace(correctedJson))
                {
                    try
                    {
                        extractedData.Dispose();
                        extractedData = JsonDocument.Parse(correctedJson);
                        extractionResult.JsonResult = correctedJson;
                    }
                    catch
                    {
                        _logger.LogWarning("[{RequestId}] Dual-pass returned invalid JSON, using first pass",
                            requestId);
                    }
                }
            }

            // ── 7. Validate ──
            var validation = _validationService.Validate(
                extractedData.RootElement,
                docTypeConfig.ValidationRules,
                fileName);
            var hasCoverageRisk = ExtractionQualityGuard.ApplyCoverageChecks(
                extractedData.RootElement, validation, pdfContent.PageCount, documentType);

            // ── 8. Extract field-level confidence ──
            var fieldConfidences = FieldConfidenceExtractor.Extract(
                extractedData,
                docTypeConfig.EffectiveConfidencePath,
                dualPassTriggered);

            // ── 9. Build response ──
            sw.Stop();

            _logger.LogInformation(
                "[{RequestId}] ✓ {File} extracted in {Ms}ms (tokens={Tokens}, dualPass={DualPass})",
                requestId, fileName, sw.ElapsedMilliseconds,
                extractionResult.TokensUsed, dualPassTriggered);

            var response = new ExtractionResponse
            {
                RequestId = requestId,
                DocumentType = documentType,
                Status = (validation.Errors.Count > 0 || hasCoverageRisk)
                    ? ExtractionStatus.PartialSuccess
                    : ExtractionStatus.Success,
                Metadata = new ExtractionMetadata
                {
                    SourceFile = fileName,
                    FileSizeBytes = pdfContent.FileSize,
                    PageCount = pdfContent.PageCount,
                    ExtractionMethod = pdfContent.ExtractionMethod,
                    ModelUsed = GetModelName(),
                    ReasoningEffort = effectiveSettings.ReasoningEffort,
                    DualPassTriggered = dualPassTriggered,
                    ExtractedAt = DateTime.UtcNow,
                    ProcessingTimeMs = sw.ElapsedMilliseconds,
                    TotalTokensUsed = extractionResult.TokensUsed,
                    PagesSentToModel = pdfContent.PageImages.Count
                },
                Data = extractedData,
                Validation = validation,
                FieldConfidences = fieldConfidences
            };

            // ── 10. Store for export retrieval ──
            _resultStore.Store(response);

            return response;
        }
        finally
        {
            // Clean up temp file
            if (!string.IsNullOrEmpty(tempFilePath) && File.Exists(tempFilePath))
            {
                try { File.Delete(tempFilePath); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Extract data from multiple documents. Processes in parallel with configured concurrency.
    /// </summary>
    public async Task<BatchExtractionResponse> ExtractBatchAsync(
        IList<(Stream Stream, string FileName, string? DocumentType)> documents,
        ExtractionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var batchId = Guid.NewGuid().ToString("N");
        var batchSw = Stopwatch.StartNew();
        var results = new System.Collections.Concurrent.ConcurrentBag<(int Index, ExtractionResponse Response)>();

        _logger.LogInformation("[Batch:{BatchId}] Starting batch extraction of {Count} documents",
            batchId, documents.Count);

        // Save all streams to temp files first
        var tempFiles = new List<(int Index, string TempPath, string FileName, string DocType)>();

        for (int i = 0; i < documents.Count; i++)
        {
            var (stream, fileName, docType) = documents[i];
            var effectiveDocType = docType ?? "invoice";
            var tempPath = await SaveToTempFileAsync(stream, fileName);
            tempFiles.Add((i, tempPath, fileName, effectiveDocType));
        }

        // Process with configured parallelism
        var semaphore = new SemaphoreSlim(_processingSettings.DefaultParallelism);

        var tasks = tempFiles.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                using var fileStream = File.OpenRead(item.TempPath);
                var response = await ExtractAsync(
                    fileStream, item.FileName, item.DocType,
                    options, $"{batchId}_{item.Index}", cancellationToken);
                results.Add((item.Index, response));
            }
            finally
            {
                semaphore.Release();
                try { File.Delete(item.TempPath); } catch { /* best effort */ }
            }
        });

        await Task.WhenAll(tasks);

        batchSw.Stop();

        var orderedResults = results.OrderBy(r => r.Index).Select(r => r.Response).ToList();
        var succeeded = orderedResults.Count(r => r.Status != ExtractionStatus.Failed);
        var failed = orderedResults.Count(r => r.Status == ExtractionStatus.Failed);

        _logger.LogInformation("[Batch:{BatchId}] Complete: {Succeeded}/{Total} succeeded in {Ms}ms",
            batchId, succeeded, documents.Count, batchSw.ElapsedMilliseconds);

        return new BatchExtractionResponse
        {
            BatchId = batchId,
            Status = failed == 0 ? ExtractionStatus.Success
                : succeeded == 0 ? ExtractionStatus.Failed
                : ExtractionStatus.PartialSuccess,
            Total = documents.Count,
            Succeeded = succeeded,
            Failed = failed,
            TotalProcessingTimeMs = batchSw.ElapsedMilliseconds,
            Results = orderedResults
        };
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private bool ShouldTriggerDualPass(JsonDocument extractedData, DocumentTypeConfig config)
    {
        if (!config.DualPass.Enabled || config.DualPass.CriticalFields.Count == 0)
            return false;

        var root = extractedData.RootElement;

        // Check critical fields for empty values
        bool criticalEmpty = config.DualPass.CriticalFields.Any(field =>
        {
            if (!root.TryGetProperty(field, out var value)) return true;
            return value.ValueKind == JsonValueKind.Null ||
                   (value.ValueKind == JsonValueKind.String &&
                    string.IsNullOrWhiteSpace(value.GetString()));
        });

        if (criticalEmpty) return true;

        // Check confidence scores
        var confidencePath = config.DualPass.ConfidencePath;
        if (!string.IsNullOrEmpty(confidencePath) &&
            root.TryGetProperty(confidencePath, out var confidence))
        {
            bool lowConfidence = config.DualPass.CriticalFields.Any(field =>
            {
                if (confidence.TryGetProperty(field, out var confValue) &&
                    confValue.ValueKind == JsonValueKind.Number)
                {
                    return confValue.GetDecimal() < config.DualPass.ConfidenceThreshold;
                }
                return false;
            });

            if (lowConfidence) return true;
        }

        return false;
    }

    private static DocumentExtractionSettings MergeSettings(
        DocumentExtractionSettings baseSettings,
        ExtractionOptions? options)
    {
        if (options == null) return baseSettings;

        return new DocumentExtractionSettings
        {
            MaxPagesForVision = options.MaxPagesForVision ?? baseSettings.MaxPagesForVision,
            MaxTextChars = options.MaxTextChars ?? baseSettings.MaxTextChars,
            TextTruncation = string.IsNullOrWhiteSpace(options.TextTruncation)
                ? baseSettings.TextTruncation
                : options.TextTruncation!,
            ReasoningEffort = options.ReasoningEffort ?? baseSettings.ReasoningEffort,
            MaxTokens = baseSettings.MaxTokens,
            Temperature = baseSettings.Temperature,
            ImageDpi = baseSettings.ImageDpi,
            ImageMaxWidthPx = baseSettings.ImageMaxWidthPx
        };
    }

    private static async Task<string> SaveToTempFileAsync(Stream stream, string fileName)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "docextractor");
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}");

        await using var fileStream = File.Create(tempFile);
        await stream.CopyToAsync(fileStream);
        return tempFile;
    }

    private string GetModelName()
    {
        // This would normally come from the settings injected into GenericOpenAIService
        return "azure-openai-gpt";
    }

    private static ExtractionResponse ErrorResponse(
        string requestId, string documentType, string fileName,
        string errorCode, string errorMessage)
    {
        return new ExtractionResponse
        {
            RequestId = requestId,
            DocumentType = documentType,
            Status = ExtractionStatus.Failed,
            Metadata = new ExtractionMetadata { SourceFile = fileName },
            Validation = new ValidationSummary { IsValid = false },
            Error = new ErrorDetail
            {
                Code = errorCode,
                Message = errorMessage
            }
        };
    }
}
