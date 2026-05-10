using System.Diagnostics;
using System.Text.Json;
using DocumentExtractionService.Core.Configuration;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Chunked extraction strategy for large multi-section documents (contracts, agreements).
///
/// PROBLEM:
///   A 33-page contract sends only 12 page images + truncated text to GPT.
///   Exhibits on pages 24–33 (often 60+ pricing line items) are never seen by the model.
///
/// SOLUTION:
///   1. Classify: scan the full document text to build a section map (main body, exhibits, signatures)
///   2. Chunk: split into intelligent page ranges aligned to section boundaries
///   3. Extract: chunk 0 gets full schema (header + line_items), chunks 1–N get line_items-only prompt
///   4. Merge: combine header from chunk 0 with all line_items, deduplicate, assemble final JSON
///
/// DESIGN PRINCIPLES:
///   - Backward compatible: if chunking is disabled or doc is small, the pipeline is untouched
///   - Chunk boundaries are section-aware: never split a table or exhibit mid-page
///   - Each chunk gets its own page images rendered at full resolution
///   - Chunks run with controlled parallelism (default 2) to balance speed vs. API rate limits
///   - The merge step handles GPT returning slightly different formats across chunks
/// </summary>
public class ChunkedExtractionStrategy
{
    private readonly IGenericOpenAIService _openAI;
    private readonly PdfProcessorService _pdfProcessor;
    private readonly ConfigurableValidationService _validationService;
    private readonly IExtractionResultStore _resultStore;
    private readonly WellPlanTwoPhaseStrategy? _wellPlanTwoPhase;
    private readonly ILogger<ChunkedExtractionStrategy> _logger;

    public ChunkedExtractionStrategy(
        IGenericOpenAIService openAI,
        PdfProcessorService pdfProcessor,
        ConfigurableValidationService validationService,
        IExtractionResultStore resultStore,
        ILogger<ChunkedExtractionStrategy> logger,
        WellPlanTwoPhaseStrategy? wellPlanTwoPhase = null)
    {
        _openAI = openAI;
        _pdfProcessor = pdfProcessor;
        _validationService = validationService;
        _resultStore = resultStore;
        _wellPlanTwoPhase = wellPlanTwoPhase;
        _logger = logger;
    }

    /// <summary>
    /// Entry point for chunked extraction. Called by GenericExtractionService
    /// when the document exceeds the chunking page threshold.
    /// </summary>
    public async Task<ExtractionResponse> ExtractChunkedAsync(
        string tempFilePath,
        PdfContent fullPdfContent,
        DocumentTypeConfig docTypeConfig,
        DocumentExtractionSettings settings,
        string requestId,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var chunkingConfig = docTypeConfig.Chunking ?? new ChunkingConfig();

        _logger.LogInformation(
            "[{RequestId}] Chunked extraction: {Pages} pages, chunkSize={ChunkSize}, threshold={Threshold}",
            requestId, fullPdfContent.PageCount, chunkingConfig.ChunkSizePages, chunkingConfig.PageThreshold);

        if (string.Equals(chunkingConfig.MergeStrategy, "two_phase", StringComparison.OrdinalIgnoreCase))
        {
            if (_wellPlanTwoPhase == null)
            {
                _logger.LogError("[{RequestId}] WellPlanTwoPhaseStrategy is not registered but merge_strategy=two_phase",
                    requestId);
                return ErrorResponse(requestId, docTypeConfig.Id, fullPdfContent.FileName,
                    "CONFIG_ERROR",
                    "Two-phase well plan extraction is configured but WellPlanTwoPhaseStrategy was not registered in DI.");
            }

            return await _wellPlanTwoPhase.ExtractAsync(
                tempFilePath, fullPdfContent, docTypeConfig, settings, requestId, cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════════
        //  STEP 1–2: Plan chunks — by fixed page windows OR by section boundaries
        // ═══════════════════════════════════════════════════════════════
        // Section-based plans can end the first chunk after only a few pages (e.g. “Main” through p.8),
        // so chunk 0’s “full” extraction never sees later exhibits. Service orders / MSAs without
        // “Exhibit” headings need page-based windows so every range gets full image coverage.

        var usePagePlan = string.Equals(chunkingConfig.ChunkPlan, "by_page", StringComparison.OrdinalIgnoreCase)
            || string.Equals(chunkingConfig.ChunkPlan, "fixed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(chunkingConfig.ChunkPlan, "fixed_pages", StringComparison.OrdinalIgnoreCase);

        List<ChunkPlan> chunks;
        if (usePagePlan)
        {
            int window = Math.Max(1, chunkingConfig.ChunkSizePages);
            chunks = PlanFixedPageChunks(fullPdfContent.PageCount, window);
            _logger.LogInformation("[{RequestId}] Chunk plan: by_page, window={Window} → {Count} chunk(s)",
                requestId, window, chunks.Count);
        }
        else
        {
            var sectionMap = ClassifySections(fullPdfContent.ExtractedText, fullPdfContent.PageCount);
            _logger.LogInformation("[{RequestId}] Section map: {Sections}",
                requestId, string.Join(" | ", sectionMap.Select(s => $"p{s.StartPage}-{s.EndPage}:{s.Label}")));
            chunks = PlanChunks(sectionMap, fullPdfContent.PageCount, chunkingConfig);
        }

        _logger.LogInformation("[{RequestId}] Planned {ChunkCount} chunks: {Chunks}",
            requestId, chunks.Count,
            string.Join(" | ", chunks.Select((c, i) => $"C{i}:p{c.StartPage}-{c.EndPage}")));

        // ═══════════════════════════════════════════════════════════════
        //  STEP 3: Extract each chunk (parallel with throttle)
        // ═══════════════════════════════════════════════════════════════

        var chunkResults = new ChunkExtractionResult[chunks.Count];
        int totalTokens = 0;
        var semaphore = new SemaphoreSlim(chunkingConfig.MaxConcurrentChunks);

        var tasks = chunks.Select(async (chunk, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await ExtractChunkAsync(
                    tempFilePath, chunk, index, chunks.Count,
                    fullPdfContent, docTypeConfig, settings,
                    requestId, cancellationToken);

                chunkResults[index] = result;
                Interlocked.Add(ref totalTokens, result.TokensUsed);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Check for total failures
        var failedChunks = chunkResults.Select((r, i) => (r, i))
            .Where(x => x.r?.Success != true).ToList();

        if (failedChunks.Count > 0)
        {
            _logger.LogWarning("[{RequestId}] {FailCount}/{Total} chunks failed: {Failed}",
                requestId, failedChunks.Count, chunks.Count,
                string.Join(", ", failedChunks.Select(x => $"C{x.i}")));
        }

        // ═══════════════════════════════════════════════════════════════
        //  STEP 4: Merge — header from chunk 0, line_items from all
        // ═══════════════════════════════════════════════════════════════

        var mergedJson = MergeChunkResults(chunkResults, docTypeConfig, requestId);

        // ═══════════════════════════════════════════════════════════════
        //  STEP 5: Validate and build response
        // ═══════════════════════════════════════════════════════════════

        JsonDocument? extractedData = null;
        try
        {
            extractedData = JsonDocument.Parse(mergedJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[{RequestId}] Merged JSON parse failed", requestId);
            return ErrorResponse(requestId, docTypeConfig.Id, fullPdfContent.FileName,
                "CHUNK_MERGE_ERROR", $"Failed to parse merged chunk results: {ex.Message}");
        }

        var validation = _validationService.Validate(
            extractedData.RootElement, docTypeConfig.ValidationRules, fullPdfContent.FileName);
        var hasCoverageRisk = ExtractionQualityGuard.ApplyCoverageChecks(
            extractedData.RootElement, validation, fullPdfContent.PageCount, docTypeConfig.Id);

        var fieldConfidences = FieldConfidenceExtractor.Extract(
            extractedData, docTypeConfig.EffectiveConfidencePath, false);

        sw.Stop();

        var successChunks = chunkResults.Count(r => r?.Success == true);
        var totalLineItems = CountLineItems(extractedData);

        _logger.LogInformation(
            "[{RequestId}] ✓ Chunked extraction complete: {Chunks} chunks ({Success} ok), " +
            "{Items} line items, {Tokens} tokens, {Ms}ms",
            requestId, chunks.Count, successChunks, totalLineItems, totalTokens, sw.ElapsedMilliseconds);

        var response = new ExtractionResponse
        {
            RequestId = requestId,
            DocumentType = docTypeConfig.Id,
            Status = (validation.Errors.Count > 0 || hasCoverageRisk)
                ? ExtractionStatus.PartialSuccess
                : ExtractionStatus.Success,
            Metadata = new ExtractionMetadata
            {
                SourceFile = fullPdfContent.FileName,
                FileSizeBytes = fullPdfContent.FileSize,
                PageCount = fullPdfContent.PageCount,
                ExtractionMethod = $"chunked({chunks.Count})",
                ModelUsed = "azure-openai-gpt",
                ReasoningEffort = settings.ReasoningEffort,
                DualPassTriggered = false,
                ExtractedAt = DateTime.UtcNow,
                ProcessingTimeMs = sw.ElapsedMilliseconds,
                TotalTokensUsed = totalTokens,
                PagesSentToModel = fullPdfContent.PageCount
            },
            Data = extractedData,
            Validation = validation,
            FieldConfidences = fieldConfidences
        };

        _resultStore.Store(response);
        return response;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SECTION CLASSIFICATION — find document structure from text
    // ═══════════════════════════════════════════════════════════════════

    private List<DocumentSection> ClassifySections(string fullText, int totalPages)
    {
        var sections = new List<DocumentSection>();
        var lines = fullText.Split('\n');

        int currentPage = 1;
        string currentSectionLabel = "Main Contract";
        int currentSectionStart = 1;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Detect page markers
            if (trimmed.StartsWith("[PAGE ") && trimmed.Contains(" of "))
            {
                var parts = trimmed.TrimStart('[').Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[1], out var pageNum))
                    currentPage = pageNum;
                continue;
            }

            // Detect section boundaries
            var detectedSection = DetectSectionChange(trimmed);
            if (detectedSection != null && detectedSection != currentSectionLabel)
            {
                // Close previous section
                sections.Add(new DocumentSection
                {
                    Label = currentSectionLabel,
                    StartPage = currentSectionStart,
                    EndPage = Math.Max(currentSectionStart, currentPage - 1)
                });

                currentSectionLabel = detectedSection;
                currentSectionStart = currentPage;
            }
        }

        // Close final section
        sections.Add(new DocumentSection
        {
            Label = currentSectionLabel,
            StartPage = currentSectionStart,
            EndPage = totalPages
        });

        // Merge very small sections (< 2 pages) into their neighbors
        return MergeSmallSections(sections);
    }

    private static string? DetectSectionChange(string line)
    {
        var upper = line.ToUpperInvariant();

        // Exhibit detection (most important — these are where pricing tables live)
        if (upper.StartsWith("EXHIBIT ") || upper.StartsWith("EXHIBIT \"") || upper.StartsWith("EXHIBIT \u201C"))
        {
            // Extract exhibit letter: 'EXHIBIT "A"', 'EXHIBIT A', 'Exhibit "E"'
            var cleaned = line
                .Replace("\"", "", StringComparison.Ordinal)
                .Replace("\u201C", "", StringComparison.Ordinal)
                .Replace("\u201D", "", StringComparison.Ordinal)
                .Trim();
            var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
                return $"Exhibit {parts[1].TrimEnd(':')}";
            return "Exhibit";
        }

        // Named exhibit pages
        if (upper.Contains("SPECIFICATIONS AND SPECIAL PROVISIONS")) return "Exhibit A";
        if (upper.Contains("EQUAL OPPORTUNITY") && upper.Contains("CLAUSE")) return "Exhibit B";
        if (upper.Contains("CONTRACTORS SPECIAL PROVISIONS") || upper.Contains("CONTRACTOR'S SPECIAL PROVISIONS"))
            return "Exhibit C";
        if (upper.Contains("STANDARD RIG TECHNOLOGY PACKAGE")) return "Exhibit E - Rig Technology";
        if (upper.Contains("PERFORMANCE DRILLING TOOLS")) return "Exhibit D - Performance Tools";
        if (upper.Contains("MANAGED PRESSURE DRILLING")) return "Exhibit F - MPD Services";
        if (upper.Contains("AUTOMATION PRODUCTS AND SERVICES")) return "Automation Products";

        // Signature page
        if (upper.Contains("ACCEPTANCE OF CONTRACT") || upper.Contains("[SIGNATURE PAGE FOLLOWS]"))
            return "Signature Page";

        // Rig inventory spec sheet (usually the last page)
        if (upper.Contains("PACE") && upper.Contains("X-") && (upper.Contains("MAST") || upper.Contains("MUD SYSTEM")))
            return "Rig Specification";

        return null;
    }

    private static List<DocumentSection> MergeSmallSections(List<DocumentSection> sections)
    {
        if (sections.Count <= 1) return sections;

        var merged = new List<DocumentSection> { sections[0] };

        for (int i = 1; i < sections.Count; i++)
        {
            var prev = merged[^1];
            var curr = sections[i];

            // Merge if current section is tiny (1 page) AND it's not an exhibit/signature
            bool isImportant = curr.Label.StartsWith("Exhibit") ||
                               curr.Label.Contains("Signature") ||
                               curr.Label.Contains("Rig Spec") ||
                               curr.Label.Contains("Automation") ||
                               curr.Label.Contains("MPD") ||
                               curr.Label.Contains("Technology");

            int sectionPages = curr.EndPage - curr.StartPage + 1;

            if (sectionPages <= 1 && !isImportant)
            {
                // Extend previous section
                merged[^1] = prev with { EndPage = curr.EndPage };
            }
            else
            {
                merged.Add(curr);
            }
        }

        return merged;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CHUNK PLANNING — split into extraction units
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Split the document into fixed page ranges (e.g. 1–12, 13–24). Ensures late pages are always
    /// included in a continuation extraction with images, unlike uneven section-based splits.
    /// </summary>
    private static List<ChunkPlan> PlanFixedPageChunks(int totalPages, int chunkSizePages)
    {
        var chunks = new List<ChunkPlan>();
        if (totalPages <= 0 || chunkSizePages <= 0) return chunks;

        for (int start = 1; start <= totalPages; start += chunkSizePages)
        {
            int end = Math.Min(start + chunkSizePages - 1, totalPages);
            chunks.Add(new ChunkPlan
            {
                StartPage = start,
                EndPage = end,
                IsFirstChunk = chunks.Count == 0,
                SectionLabels = [$"pages_{start}_{end}"]
            });
        }

        return chunks;
    }

    private static List<ChunkPlan> PlanChunks(
        List<DocumentSection> sections, int totalPages, ChunkingConfig config)
    {
        var chunks = new List<ChunkPlan>();
        int chunkSize = config.ChunkSizePages;

        // Strategy: walk sections, accumulate pages until we hit chunkSize,
        // then close the chunk at the section boundary

        int currentChunkStart = 1;
        int accumulatedPages = 0;
        var sectionsInCurrentChunk = new List<string>();

        foreach (var section in sections)
        {
            int sectionPages = section.EndPage - section.StartPage + 1;

            // If adding this section would exceed chunk size AND we already have content → close chunk
            if (accumulatedPages > 0 && accumulatedPages + sectionPages > chunkSize)
            {
                chunks.Add(new ChunkPlan
                {
                    StartPage = currentChunkStart,
                    EndPage = section.StartPage - 1,
                    IsFirstChunk = chunks.Count == 0,
                    SectionLabels = new List<string>(sectionsInCurrentChunk)
                });

                currentChunkStart = section.StartPage;
                accumulatedPages = 0;
                sectionsInCurrentChunk.Clear();
            }

            accumulatedPages += sectionPages;
            sectionsInCurrentChunk.Add(section.Label);
        }

        // Close final chunk
        if (accumulatedPages > 0)
        {
            chunks.Add(new ChunkPlan
            {
                StartPage = currentChunkStart,
                EndPage = totalPages,
                IsFirstChunk = chunks.Count == 0,
                SectionLabels = new List<string>(sectionsInCurrentChunk)
            });
        }

        // Safety: if we ended up with just 1 chunk, no need for chunking
        // But still return it — the caller will merge normally

        return chunks;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CHUNK EXTRACTION — process one page range
    // ═══════════════════════════════════════════════════════════════════

    private async Task<ChunkExtractionResult> ExtractChunkAsync(
        string tempFilePath,
        ChunkPlan chunk,
        int chunkIndex,
        int totalChunks,
        PdfContent fullPdfContent,
        DocumentTypeConfig docTypeConfig,
        DocumentExtractionSettings settings,
        string requestId,
        CancellationToken cancellationToken)
    {
        var chunkId = $"{requestId}_C{chunkIndex}";

        _logger.LogInformation(
            "[{ChunkId}] Extracting chunk {Index}/{Total}: pages {Start}-{End} ({Sections})",
            chunkId, chunkIndex + 1, totalChunks, chunk.StartPage, chunk.EndPage,
            string.Join(", ", chunk.SectionLabels));

        try
        {
            // ── Build PdfContent for this page range ──
            var chunkPdf = _pdfProcessor.ProcessPdfPageRange(
                tempFilePath,
                chunk.StartPage,
                chunk.EndPage,
                new PdfProcessingOptions
                {
                    MaxPagesForVision = chunk.EndPage - chunk.StartPage + 1, // ALL pages in chunk get images
                    ImageDpi = settings.ImageDpi,
                    ImageMaxWidthPx = settings.ImageMaxWidthPx,
                    MaxTextChars = 0, // no truncation within a chunk
                    UseDualTextExtraction = string.Equals(
                        docTypeConfig.Id, "invoice", StringComparison.OrdinalIgnoreCase)
                });

            // Override filename and total page count for prompt template
            chunkPdf.FileName = fullPdfContent.FileName;

            // ── Choose prompt strategy ──
            string systemPrompt;
            string extractionPrompt;
            string schema;

            if (chunk.IsFirstChunk)
            {
                // Chunk 0: full schema extraction (header + line_items)
                systemPrompt = docTypeConfig.SystemPrompt;
                extractionPrompt = BuildChunkPrompt(
                    docTypeConfig.ExtractionPromptTemplate,
                    chunkPdf, chunk, chunkIndex, totalChunks, fullPdfContent.PageCount,
                    isFirstChunk: true);
                schema = docTypeConfig.JsonSchema;
            }
            else
            {
                // Chunks 1–N: line_items-only extraction
                systemPrompt = BuildContinuationSystemPrompt(docTypeConfig.SystemPrompt);
                extractionPrompt = BuildChunkPrompt(
                    docTypeConfig.ChunkExtractionPrompt ?? docTypeConfig.ExtractionPromptTemplate,
                    chunkPdf, chunk, chunkIndex, totalChunks, fullPdfContent.PageCount,
                    isFirstChunk: false);
                schema = BuildLineItemsOnlySchema(docTypeConfig.JsonSchema);
            }

            // ── Call GPT ──
            var result = await _openAI.ExtractAsync(
                chunkPdf,
                systemPrompt,
                extractionPrompt,
                schema,
                settings,
                cancellationToken);

            _logger.LogInformation(
                "[{ChunkId}] ✓ Chunk {Index} extracted: {Tokens} tokens",
                chunkId, chunkIndex + 1, result.TokensUsed);

            return new ChunkExtractionResult
            {
                Success = true,
                ChunkIndex = chunkIndex,
                JsonResult = result.JsonResult,
                TokensUsed = result.TokensUsed,
                StartPage = chunk.StartPage,
                EndPage = chunk.EndPage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ChunkId}] Chunk {Index} failed", chunkId, chunkIndex + 1);
            return new ChunkExtractionResult
            {
                Success = false,
                ChunkIndex = chunkIndex,
                Error = ex.Message,
                StartPage = chunk.StartPage,
                EndPage = chunk.EndPage
            };
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PROMPT BUILDING — context-aware prompts per chunk
    // ═══════════════════════════════════════════════════════════════════

    private string BuildChunkPrompt(
        string basePrompt, PdfContent chunkPdf, ChunkPlan chunk,
        int chunkIndex, int totalChunks, int totalDocPages,
        bool isFirstChunk)
    {
        // Replace template variables
        var prompt = basePrompt
            .Replace("{{FILE_NAME}}", chunkPdf.FileName)
            .Replace("{{PAGE_COUNT}}", totalDocPages.ToString())
            .Replace("{{IS_SCANNED}}", chunkPdf.IsScanned.ToString().ToLower())
            .Replace("{{EXTRACTION_METHOD}}", chunkPdf.ExtractionMethod)
            .Replace("{{FILE_SIZE_KB}}", (chunkPdf.FileSize / 1024).ToString());

        // Add chunk context header
        var chunkContext = new System.Text.StringBuilder();
        chunkContext.AppendLine();
        chunkContext.AppendLine("══════════════════════════════════════════════════════════════");
        chunkContext.AppendLine($"  CHUNK {chunkIndex + 1} of {totalChunks}");
        chunkContext.AppendLine($"  Pages: {chunk.StartPage}–{chunk.EndPage} of {totalDocPages} total");
        chunkContext.AppendLine($"  Sections: {string.Join(", ", chunk.SectionLabels)}");
        chunkContext.AppendLine("══════════════════════════════════════════════════════════════");

        if (!isFirstChunk)
        {
            chunkContext.AppendLine();
            chunkContext.AppendLine("IMPORTANT: You are extracting a CONTINUATION of a larger document.");
            chunkContext.AppendLine("The contract header, parties, and initial rates were extracted from earlier pages.");
            chunkContext.AppendLine("Extract ONLY the line_items (rates, fees, prices) visible on THESE pages.");
            chunkContext.AppendLine("Output ONLY a JSON object with a single \"line_items\" array.");
            chunkContext.AppendLine("Use the correct page numbers (these are pages " +
                $"{chunk.StartPage}–{chunk.EndPage} of the original document).");
        }

        return chunkContext.ToString() + "\n" + prompt;
    }

    private static string BuildContinuationSystemPrompt(string fullSystemPrompt)
    {
        // Take the first ~40% of the system prompt (rules about how to extract)
        // and add continuation instructions
        var lines = fullSystemPrompt.Split('\n');
        var ruleLines = lines.TakeWhile(l =>
            !l.Contains("STEP 1") && !l.Contains("CONTRACT IDENTITY")).ToList();

        var continuation = string.Join('\n', ruleLines);

        continuation += @"

═══════════════════════════════════════════════════════════════
CONTINUATION EXTRACTION MODE
═══════════════════════════════════════════════════════════════

You are extracting a PAGE RANGE from a larger document. The contract header
and initial commercial terms were already extracted from earlier pages.

YOUR TASK: Extract ONLY rates, fees, prices, and commercial terms from
the pages provided. Output a JSON object with a single ""line_items"" array.

RULES:
- Extract every rate, fee, price, and cost you can see
- Use the actual page numbers from the original document
- Group items by the section/exhibit they appear in
- Include the same fields: section, description, amount, unit, notes, page_ref
- Do NOT include contract header fields — those come from the first chunk
- Do NOT guess — if a value is not visible on these pages, skip it
";

        return continuation;
    }

    private static string BuildLineItemsOnlySchema(string fullSchema)
    {
        // Build a minimal schema that only requires line_items array
        // This prevents GPT from trying to fill header fields and wasting tokens
        return @"{
  ""type"": ""object"",
  ""additionalProperties"": true,
  ""required"": [""line_items""],
  ""properties"": {
    ""line_items"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""additionalProperties"": false,
        ""required"": [""section"", ""description"", ""amount"", ""unit"", ""notes"", ""page_ref""],
        ""properties"": {
          ""section"":     { ""type"": [""string"", ""null""] },
          ""description"": { ""type"": [""string"", ""null""] },
          ""amount"":      { ""anyOf"": [{ ""type"": ""number"" }, { ""type"": ""string"" }, { ""type"": ""null"" }] },
          ""unit"":        { ""type"": [""string"", ""null""] },
          ""notes"":       { ""type"": [""string"", ""null""] },
          ""page_ref"":    { ""anyOf"": [{ ""type"": ""integer"" }, { ""type"": ""null"" }] }
        }
      }
    }
  }
}";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MERGE — combine all chunks into final JSON
    // ═══════════════════════════════════════════════════════════════════

    private string MergeChunkResults(
        ChunkExtractionResult[] chunkResults, DocumentTypeConfig docTypeConfig, string requestId)
    {
        // Start with chunk 0 as the base (has header fields)
        var baseResult = chunkResults[0];
        if (baseResult?.Success != true || string.IsNullOrEmpty(baseResult.JsonResult))
        {
            _logger.LogError("[{RequestId}] Chunk 0 (header) failed — cannot merge", requestId);

            // Try to find any successful chunk as fallback
            var fallback = chunkResults.FirstOrDefault(r => r?.Success == true);
            return fallback?.JsonResult ?? "{}";
        }

        JsonDocument baseDoc;
        try
        {
            baseDoc = JsonDocument.Parse(baseResult.JsonResult);
        }
        catch
        {
            return baseResult.JsonResult; // Return raw if parse fails
        }

        // Collect all line_items from all chunks
        var allLineItems = new List<JsonElement>();

        foreach (var chunk in chunkResults)
        {
            if (chunk?.Success != true || string.IsNullOrEmpty(chunk.JsonResult)) continue;

            try
            {
                using var doc = JsonDocument.Parse(chunk.JsonResult);
                var root = doc.RootElement;

                if (root.TryGetProperty("line_items", out var items) &&
                    items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        // Clone since the JsonDocument will be disposed
                        allLineItems.Add(item.Clone());
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[{RequestId}] Failed to parse chunk {Index} for merge",
                    requestId, chunk.ChunkIndex);
            }
        }

        // Deduplicate line items by description (GPT sometimes extracts the same
        // item in overlapping chunks or from both text and images)
        var deduplicated = DeduplicateLineItems(allLineItems);

        _logger.LogInformation("[{RequestId}] Merge: {Raw} raw items → {Dedup} after dedup from {Chunks} chunks",
            requestId, allLineItems.Count, deduplicated.Count, chunkResults.Count(r => r?.Success == true));

        // Build merged JSON: take all fields from chunk 0, replace line_items
        var writer = new System.IO.StringWriter();
        using var jsonWriter = new System.Text.Json.Utf8JsonWriter(
            new Utf8JsonWriterStream(writer),
            new JsonWriterOptions { Indented = true });

        jsonWriter.WriteStartObject();

        foreach (var prop in baseDoc.RootElement.EnumerateObject())
        {
            if (prop.Name == "line_items")
            {
                // Replace with merged line items
                jsonWriter.WritePropertyName("line_items");
                jsonWriter.WriteStartArray();
                foreach (var item in deduplicated)
                {
                    item.WriteTo(jsonWriter);
                }
                jsonWriter.WriteEndArray();
            }
            else
            {
                prop.WriteTo(jsonWriter);
            }
        }

        jsonWriter.WriteEndObject();
        jsonWriter.Flush();

        baseDoc.Dispose();

        return writer.ToString();
    }

    private List<JsonElement> DeduplicateLineItems(List<JsonElement> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<JsonElement>();

        foreach (var item in items)
        {
            // Build a dedup key from description + amount
            var desc = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String
                ? d.GetString() ?? "" : "";
            var amt = item.TryGetProperty("amount", out var a)
                ? (a.ValueKind == JsonValueKind.Number ? a.GetDouble().ToString("F2")
                   : a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : "")
                : "";

            var key = $"{desc.Trim()}|{amt.Trim()}";

            if (string.IsNullOrWhiteSpace(desc)) continue; // Skip empty items

            if (seen.Add(key))
            {
                result.Add(item);
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static int CountLineItems(JsonDocument doc)
    {
        if (doc.RootElement.TryGetProperty("line_items", out var items) &&
            items.ValueKind == JsonValueKind.Array)
            return items.GetArrayLength();
        return 0;
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
            Error = new ErrorDetail { Code = errorCode, Message = errorMessage }
        };
    }

    // ── Internal models ──

    private record DocumentSection
    {
        public string Label { get; init; } = "";
        public int StartPage { get; init; }
        public int EndPage { get; init; }
    }

    private class ChunkPlan
    {
        public int StartPage { get; init; }
        public int EndPage { get; init; }
        public bool IsFirstChunk { get; init; }
        public List<string> SectionLabels { get; init; } = [];
    }

    private class ChunkExtractionResult
    {
        public bool Success { get; init; }
        public int ChunkIndex { get; init; }
        public string JsonResult { get; init; } = "";
        public int TokensUsed { get; init; }
        public string? Error { get; init; }
        public int StartPage { get; init; }
        public int EndPage { get; init; }
    }
}

/// <summary>
/// Adapter to let Utf8JsonWriter write to a StringWriter via a Stream.
/// </summary>
internal class Utf8JsonWriterStream : Stream
{
    private readonly System.IO.StringWriter _writer;
    public Utf8JsonWriterStream(System.IO.StringWriter writer) => _writer = writer;

    public override void Write(byte[] buffer, int offset, int count)
        => _writer.Write(System.Text.Encoding.UTF8.GetString(buffer, offset, count));

    public override void Flush() => _writer.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
