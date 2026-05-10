using System.Diagnostics;
using System.Text.Json;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Two-phase extraction strategy for well plan documents.
///
/// WHY NOT CHUNK-AND-MERGE:
///   A single well's data (header p3, casing p15, BHA p25, formations p35)
///   spans the entire document. Chunk-and-merge requires fuzzy matching by
///   well_name across independent GPT calls — GPT formats names differently
///   across calls, creating duplicates instead of merged wells.
///
/// TWO-PHASE APPROACH:
///   Phase 1 — DOCUMENT MAP (cheap, text-only, one call):
///     Send ALL extracted text (no images) with a lightweight prompt:
///     "List every well and which pages contain its data."
///     Returns: [{well_name, pages[]}, ...]
///
///   Phase 2 — PER-WELL EXTRACTION (targeted, with images, parallel):
///     For each well, render ONLY that well's pages as images.
///     Extract with the full well schema but for ONE well only.
///     Each call sees exactly the right pages — no waste, no ambiguity.
///
///   Assembly — trivial:
///     Each Phase 2 call returns one complete well object.
///     Collect into the wells[] array. No fuzzy matching needed.
///
/// ADVANTAGES:
///   - Well name is authoritative (set once in Phase 1, used as context in Phase 2)
///   - Each GPT call sees only relevant pages → higher accuracy
///   - No cross-page context loss (cover page + casing + BHA all in one call)
///   - Parallel per-well calls (default: 2 concurrent)
///   - Works for 5-well pads AND 100-page single-well programs
/// </summary>
public class WellPlanTwoPhaseStrategy
{
    private readonly IGenericOpenAIService _openAI;
    private readonly PdfProcessorService _pdfProcessor;
    private readonly ConfigurableValidationService _validationService;
    private readonly IExtractionResultStore _resultStore;
    private readonly ILogger<WellPlanTwoPhaseStrategy> _logger;

    public WellPlanTwoPhaseStrategy(
        IGenericOpenAIService openAI,
        PdfProcessorService pdfProcessor,
        ConfigurableValidationService validationService,
        IExtractionResultStore resultStore,
        ILogger<WellPlanTwoPhaseStrategy> logger)
    {
        _openAI = openAI;
        _pdfProcessor = pdfProcessor;
        _validationService = validationService;
        _resultStore = resultStore;
        _logger = logger;
    }

    /// <summary>
    /// Entry point — called by ChunkedExtractionStrategy when merge_strategy = "two_phase".
    /// </summary>
    public async Task<ExtractionResponse> ExtractAsync(
        string tempFilePath,
        PdfContent fullPdfContent,
        DocumentTypeConfig docTypeConfig,
        DocumentExtractionSettings settings,
        string requestId,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var chunking = docTypeConfig.Chunking!;
        int totalTokens = 0;

        _logger.LogInformation(
            "[{RequestId}] Two-phase well plan extraction: {Pages} pages, {File}",
            requestId, fullPdfContent.PageCount, fullPdfContent.FileName);

        // ═══════════════════════════════════════════════════════════════
        //  PHASE 1: Document Map — text-only scan for well→page mapping
        // ═══════════════════════════════════════════════════════════════

        _logger.LogInformation("[{RequestId}] Phase 1: Building document map (text-only)...", requestId);

        List<WellPageMap> wellMap;
        string? documentMapJson = null;

        try
        {
            // Build a text-only PdfContent (no images → cheap call)
            var mapPdf = new PdfContent
            {
                FileName = fullPdfContent.FileName,
                FilePath = fullPdfContent.FilePath,
                FileSize = fullPdfContent.FileSize,
                PageCount = fullPdfContent.PageCount,
                IsScanned = fullPdfContent.IsScanned,
                ExtractedText = fullPdfContent.ExtractedText,
                PageImages = new List<byte[]>(), // NO images — text scan only
                ExtractionMethod = "text_scan"
            };

            // If document is scanned (no text), we need images for Phase 1 too
            // Send first and last few pages as images for cover + tail
            if (fullPdfContent.IsScanned || fullPdfContent.ExtractedText.Length < 2000)
            {
                _logger.LogInformation("[{RequestId}] Document appears scanned — using images for Phase 1", requestId);
                var scannedPages = _pdfProcessor.ProcessPdfPageRange(
                    tempFilePath, 1, Math.Min(fullPdfContent.PageCount, 20),
                    new PdfProcessingOptions
                    {
                        MaxPagesForVision = 20,
                        ImageDpi = 150,  // lower res for map — just need to read headers
                        ImageMaxWidthPx = 1200
                    });
                mapPdf.PageImages = scannedPages.PageImages;
                mapPdf.ExtractionMethod = "vision_scan";
            }

            var mapSystemPrompt = BuildMapSystemPrompt();
            var mapExtractionPrompt = docTypeConfig.MapPrompt
                ?? BuildDefaultMapPrompt(fullPdfContent);
            var mapSchema = docTypeConfig.MapSchema
                ?? GetDefaultMapSchema();

            var mapResult = await _openAI.ExtractAsync(
                mapPdf, mapSystemPrompt, mapExtractionPrompt, mapSchema,
                new DocumentExtractionSettings
                {
                    MaxTokens = 4096,
                    ReasoningEffort = "medium",
                    Temperature = 0,
                    ImageDpi = settings.ImageDpi,
                    ImageMaxWidthPx = settings.ImageMaxWidthPx
                },
                ct);

            totalTokens += mapResult.TokensUsed;
            documentMapJson = mapResult.JsonResult;
            wellMap = ParseWellMap(mapResult.JsonResult);

            _logger.LogInformation(
                "[{RequestId}] Phase 1 complete: {Wells} wells mapped, {Tokens} tokens",
                requestId, wellMap.Count, mapResult.TokensUsed);

            foreach (var w in wellMap)
            {
                _logger.LogDebug("[{RequestId}]   {Well}: pages [{Pages}]",
                    requestId, w.WellName, string.Join(",", w.Pages));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{RequestId}] Phase 1 failed — falling back to single-pass", requestId);
            return FallbackSinglePass(tempFilePath, fullPdfContent, docTypeConfig, settings, requestId, ct);
        }

        if (wellMap.Count == 0)
        {
            _logger.LogWarning("[{RequestId}] Phase 1 found no wells — falling back to single-pass", requestId);
            return FallbackSinglePass(tempFilePath, fullPdfContent, docTypeConfig, settings, requestId, ct);
        }

        // ═══════════════════════════════════════════════════════════════
        //  PHASE 2: Per-well extraction — targeted pages with images
        // ═══════════════════════════════════════════════════════════════

        _logger.LogInformation("[{RequestId}] Phase 2: Extracting {Count} wells...", requestId, wellMap.Count);

        // Also extract document-level header info from Phase 1 map or first pages
        JsonElement? headerData = null;
        if (!string.IsNullOrEmpty(documentMapJson))
        {
            try
            {
                var mapDoc = JsonDocument.Parse(documentMapJson);
                headerData = mapDoc.RootElement.Clone();
            }
            catch { /* ignore — header will come from well extraction */ }
        }

        var wellResults = new (string WellName, JsonElement? Data, int Tokens)[wellMap.Count];
        var semaphore = new SemaphoreSlim(chunking.MaxConcurrentChunks);

        var tasks = wellMap.Select(async (well, index) =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await ExtractSingleWellAsync(
                    tempFilePath, well, fullPdfContent, docTypeConfig, settings,
                    requestId, index, wellMap.Count, ct);

                wellResults[index] = result;
                Interlocked.Add(ref totalTokens, result.Tokens);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // ═══════════════════════════════════════════════════════════════
        //  ASSEMBLY: Combine header + all wells into final JSON
        // ═══════════════════════════════════════════════════════════════

        var assembledJson = AssembleFinalResult(
            wellResults, headerData, docTypeConfig, fullPdfContent, requestId);

        JsonDocument? extractedData = null;
        try
        {
            extractedData = JsonDocument.Parse(assembledJson);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[{RequestId}] Assembly JSON parse failed", requestId);
            return ErrorResponse(requestId, docTypeConfig.Id, fullPdfContent.FileName,
                "ASSEMBLY_ERROR", $"Failed to parse assembled result: {ex.Message}");
        }

        var validation = _validationService.Validate(
            extractedData.RootElement, docTypeConfig.ValidationRules, fullPdfContent.FileName);

        var fieldConfidences = FieldConfidenceExtractor.Extract(
            extractedData, docTypeConfig.EffectiveConfidencePath, false);

        sw.Stop();

        var successWells = wellResults.Count(r => r.Data != null);

        _logger.LogInformation(
            "[{RequestId}] ✓ Two-phase complete: {Wells}/{Total} wells extracted, " +
            "{Tokens} total tokens, {Ms}ms",
            requestId, successWells, wellMap.Count, totalTokens, sw.ElapsedMilliseconds);

        var response = new ExtractionResponse
        {
            RequestId = requestId,
            DocumentType = docTypeConfig.Id,
            Status = validation.Errors.Count > 0
                ? ExtractionStatus.PartialSuccess : ExtractionStatus.Success,
            Metadata = new ExtractionMetadata
            {
                SourceFile = fullPdfContent.FileName,
                FileSizeBytes = fullPdfContent.FileSize,
                PageCount = fullPdfContent.PageCount,
                ExtractionMethod = $"two_phase({wellMap.Count} wells)",
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
    //  Phase 2: Extract a single well from its specific pages
    // ═══════════════════════════════════════════════════════════════════

    private async Task<(string WellName, JsonElement? Data, int Tokens)> ExtractSingleWellAsync(
        string tempFilePath,
        WellPageMap well,
        PdfContent fullPdfContent,
        DocumentTypeConfig docTypeConfig,
        DocumentExtractionSettings settings,
        string requestId,
        int wellIndex,
        int totalWells,
        CancellationToken ct)
    {
        var wellId = $"{requestId}_W{wellIndex}";

        _logger.LogInformation(
            "[{WellId}] Extracting well {Index}/{Total}: '{Name}' from pages [{Pages}]",
            wellId, wellIndex + 1, totalWells, well.WellName,
            string.Join(",", well.Pages));

        try
        {
            // ── Build PdfContent with ONLY this well's pages ──
            // Group consecutive pages into ranges for efficient rendering
            var ranges = GroupConsecutivePages(well.Pages);
            var wellPdf = new PdfContent
            {
                FileName = fullPdfContent.FileName,
                FilePath = fullPdfContent.FilePath,
                FileSize = fullPdfContent.FileSize,
                PageCount = fullPdfContent.PageCount,
                IsScanned = fullPdfContent.IsScanned,
                ExtractionMethod = "targeted",
                PageImages = new List<byte[]>(),
                ExtractedText = ""
            };

            // Render images and extract text for each page range
            var textBuilder = new System.Text.StringBuilder();
            foreach (var (start, end) in ranges)
            {
                var rangePdf = _pdfProcessor.ProcessPdfPageRange(
                    tempFilePath, start, end,
                    new PdfProcessingOptions
                    {
                        MaxPagesForVision = end - start + 1,
                        ImageDpi = settings.ImageDpi,
                        ImageMaxWidthPx = settings.ImageMaxWidthPx
                    });

                wellPdf.PageImages.AddRange(rangePdf.PageImages);
                if (!string.IsNullOrEmpty(rangePdf.ExtractedText))
                    textBuilder.AppendLine(rangePdf.ExtractedText);
            }
            wellPdf.ExtractedText = textBuilder.ToString();

            _logger.LogDebug("[{WellId}] Rendered {Images} images, {TextLen} text chars for {Well}",
                wellId, wellPdf.PageImages.Count, wellPdf.ExtractedText.Length, well.WellName);

            // ── Build per-well prompt ──
            var perWellPrompt = docTypeConfig.PerWellPrompt
                ?? BuildDefaultPerWellPrompt(well, fullPdfContent);

            // Inject template variables
            perWellPrompt = perWellPrompt
                .Replace("{{FILE_NAME}}", fullPdfContent.FileName)
                .Replace("{{PAGE_COUNT}}", fullPdfContent.PageCount.ToString())
                .Replace("{{IS_SCANNED}}", wellPdf.IsScanned ? "Yes" : "No")
                .Replace("{{EXTRACTION_METHOD}}", wellPdf.ExtractionMethod)
                .Replace("{{WELL_NAME}}", well.WellName)
                .Replace("{{WELL_PAGES}}", string.Join(", ", well.Pages));

            // ── Use the single-well schema (wraps the well object in a wells[] array) ──
            var schema = docTypeConfig.PerWellSchema ?? BuildSingleWellSchema(docTypeConfig.JsonSchema);

            // ── Call GPT ──
            var result = await _openAI.ExtractAsync(
                wellPdf,
                docTypeConfig.SystemPrompt,
                perWellPrompt,
                schema,
                settings,
                ct);

            _logger.LogInformation("[{WellId}] ✓ Well '{Name}' extracted: {Tokens} tokens",
                wellId, well.WellName, result.TokensUsed);

            // Parse and extract the well object
            using var doc = JsonDocument.Parse(result.JsonResult);
            var root = doc.RootElement;

            // The response should have a "wells" array with one item
            JsonElement? wellData = null;
            if (root.TryGetProperty("wells", out var wells) &&
                wells.ValueKind == JsonValueKind.Array &&
                wells.GetArrayLength() > 0)
            {
                wellData = wells[0].Clone();
            }
            else if (root.TryGetProperty("well_name", out _))
            {
                // Response is the well object directly (no wrapper)
                wellData = root.Clone();
            }

            return (well.WellName, wellData, result.TokensUsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{WellId}] Failed to extract well '{Name}'", wellId, well.WellName);
            return (well.WellName, null, 0);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Assembly: header + wells[] into final document JSON
    // ═══════════════════════════════════════════════════════════════════

    private string AssembleFinalResult(
        (string WellName, JsonElement? Data, int Tokens)[] wellResults,
        JsonElement? headerData,
        DocumentTypeConfig docTypeConfig,
        PdfContent fullPdf,
        string requestId)
    {
        var writer = new System.IO.StringWriter();
        using var jw = new Utf8JsonWriter(
            new Utf8JsonWriterStream(writer),
            new JsonWriterOptions { Indented = true });

        jw.WriteStartObject();

        // Write header fields from Phase 1 map response (if available)
        if (headerData.HasValue && headerData.Value.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in headerData.Value.EnumerateObject())
            {
                // Skip the wells_page_map and wells — we'll write our own wells array
                if (prop.Name is "wells_page_map" or "wells" or "well_map") continue;
                prop.WriteTo(jw);
            }
        }
        else
        {
            // Minimal header
            jw.WriteString("document_type", "well_plan");
        }

        // Write wells array from Phase 2 results
        jw.WritePropertyName("wells");
        jw.WriteStartArray();

        int wellCount = 0;
        foreach (var (wellName, data, _) in wellResults)
        {
            if (data == null)
            {
                _logger.LogWarning("[{RequestId}] Well '{Name}' has no data — skipping", requestId, wellName);
                continue;
            }

            data.Value.WriteTo(jw);
            wellCount++;
        }

        jw.WriteEndArray();

        // Write confidence section
        jw.WritePropertyName("confidence");
        jw.WriteStartObject();
        jw.WriteNumber("rig_name", headerData.HasValue ? 0.9 : 0.5);
        jw.WriteNumber("wells", wellCount > 0 ? 0.9 : 0.1);
        jw.WriteEndObject();

        jw.WriteEndObject();
        jw.Flush();

        _logger.LogInformation("[{RequestId}] Assembled {Wells} wells into final JSON", requestId, wellCount);
        return writer.ToString();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Phase 1: Prompts and schema
    // ═══════════════════════════════════════════════════════════════════

    private static string BuildMapSystemPrompt()
    {
        return @"You are a well plan document analyst. Your task is to scan the entire document
and identify every well mentioned, along with which pages contain data for each well.

You also extract the document-level header information (rig, operator, pad, location).

RULES:
- Include ALL wells, even if they appear only briefly
- Page numbers must be 1-based, matching [PAGE N of M] markers in the text
- For multi-well pad tables where wells are columns, list the page for EVERY well in that table
- Include cover pages, summary pages, schematic pages, and appendix pages
- A well may appear on many non-consecutive pages (cover p1, casing p15, BHA p25)
- Return the EXACT well name as it appears in the document — do not reformat
- For single-well programs, there is one well entry with potentially many pages";
    }

    private static string BuildDefaultMapPrompt(PdfContent pdf)
    {
        return $@"Scan this well plan document and build a map of all wells and their page locations.

FILE: {pdf.FileName} | PAGES: {pdf.PageCount}

STEP 1: Extract document header
- rig_name, operator, pad_name, location, depth_unit, document_format, language

STEP 2: Find every well
- Scan [PAGE N] markers and content to identify each well name
- Record which pages contain data for each well
- Include: cover/summary pages, technical data pages, schematic pages, appendices

STEP 3: Output the map
Return JSON with the document header fields plus a wells_page_map array.";
    }

    private static string GetDefaultMapSchema()
    {
        return @"{
  ""type"": ""object"",
  ""additionalProperties"": false,
  ""required"": [""document_type"", ""rig_name"", ""operator"", ""pad_name"", ""location"", ""depth_unit"", ""document_format"", ""language"", ""wells_page_map""],
  ""properties"": {
    ""document_type"": { ""type"": ""string"" },
    ""rig_name"": { ""type"": [""string"", ""null""] },
    ""operator"": { ""type"": [""string"", ""null""] },
    ""pad_name"": { ""type"": [""string"", ""null""] },
    ""location"": { ""type"": [""string"", ""null""] },
    ""depth_unit"": { ""type"": [""string"", ""null""] },
    ""document_format"": { ""type"": [""string"", ""null""] },
    ""language"": { ""type"": [""string"", ""null""] },
    ""report_status"": { ""type"": [""string"", ""null""] },
    ""report_date"": { ""type"": [""string"", ""null""] },
    ""wells_page_map"": {
      ""type"": ""array"",
      ""items"": {
        ""type"": ""object"",
        ""additionalProperties"": false,
        ""required"": [""well_name"", ""pages""],
        ""properties"": {
          ""well_name"": { ""type"": ""string"" },
          ""pages"": { ""type"": ""array"", ""items"": { ""type"": ""integer"" } },
          ""well_type"": { ""type"": [""string"", ""null""] },
          ""total_depth"": { ""type"": [""string"", ""null""] }
        }
      }
    }
  }
}";
    }

    private static string BuildDefaultPerWellPrompt(WellPageMap well, PdfContent fullPdf)
    {
        return $@"Extract ALL technical data for well ""{well.WellName}"" from the attached pages.

FILE: {fullPdf.FileName} | TOTAL PAGES: {fullPdf.PageCount}
WELL: {well.WellName}
PAGES PROVIDED: {string.Join(", ", well.Pages)}

These pages were selected because they contain data for this specific well.
Extract everything you can see:

- Well identity: well_name, well_type, api_number, afe_number, total_depth_md, total_depth_tvd
- Surface coordinates, ground level, RKB
- Casing program: every casing section with hole_size, OD, ID, grade, weight, connection, depths, cement
- Formation tops: every formation with name, MD, TVD
- Drilling sections: hole_size, depth range, WOB, RPM, flow_rate, ROP, BHA type, bit
- Drilling fluids: per section — type, design MW, min/max MW
- Risks and hazards: per section
- Any notes visible on these pages

IMPORTANT:
- The well_name MUST be exactly: ""{well.WellName}""
- Extract data ONLY for this well — ignore other wells if visible
- Use null for fields not present on these pages
- Use the actual page numbers from the original document

Output a JSON object with a ""wells"" array containing ONE well object.";
    }

    /// <summary>
    /// Build a schema wrapper that accepts a single well in a wells[] array.
    /// Reuses the well item schema from the full document schema.
    /// </summary>
    private static string BuildSingleWellSchema(string fullSchema)
    {
        try
        {
            using var doc = JsonDocument.Parse(fullSchema);
            var root = doc.RootElement;

            if (root.TryGetProperty("properties", out var props) &&
                props.TryGetProperty("wells", out var wellsProp) &&
                wellsProp.TryGetProperty("items", out var wellItemSchema))
            {
                // Wrap the well item schema in a minimal document
                return $$"""
{
  "type": "object",
  "additionalProperties": false,
  "required": ["wells"],
  "properties": {
    "wells": {
      "type": "array",
      "items": {{wellItemSchema.GetRawText()}}
    }
  }
}
""";
            }
        }
        catch { /* fall through */ }

        // Fallback: return full schema (less efficient but works)
        return fullSchema;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Fallback: single-pass extraction if Phase 1 fails
    // ═══════════════════════════════════════════════════════════════════

    private ExtractionResponse FallbackSinglePass(
        string tempFilePath,
        PdfContent fullPdfContent,
        DocumentTypeConfig docTypeConfig,
        DocumentExtractionSettings settings,
        string requestId,
        CancellationToken ct)
    {
        _logger.LogWarning("[{RequestId}] Falling back to single-pass (no chunking)", requestId);

        // This returns a response via the normal pipeline
        // The caller (ChunkedExtractionStrategy) should catch this and re-route
        // For now, return an error that signals the caller to fall through
        return new ExtractionResponse
        {
            RequestId = requestId,
            DocumentType = docTypeConfig.Id,
            Status = ExtractionStatus.Failed,
            Metadata = new ExtractionMetadata { SourceFile = fullPdfContent.FileName },
            Validation = new ValidationSummary { IsValid = false },
            Error = new ErrorDetail
            {
                Code = "TWO_PHASE_FALLBACK",
                Message = "Phase 1 mapping failed — document should be extracted with standard pipeline"
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static List<WellPageMap> ParseWellMap(string json)
    {
        var wells = new List<WellPageMap>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("wells_page_map", out var mapArr) ||
                mapArr.ValueKind != JsonValueKind.Array)
                return wells;

            foreach (var item in mapArr.EnumerateArray())
            {
                var name = item.TryGetProperty("well_name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;

                var pages = new List<int>();
                if (item.TryGetProperty("pages", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in pArr.EnumerateArray())
                    {
                        if (p.ValueKind == JsonValueKind.Number)
                            pages.Add(p.GetInt32());
                    }
                }

                if (pages.Count > 0)
                    wells.Add(new WellPageMap { WellName = name, Pages = pages });
            }
        }
        catch { /* return empty */ }

        return wells;
    }

    /// <summary>
    /// Group sorted page numbers into consecutive ranges for efficient rendering.
    /// [3, 5, 15, 16, 17, 25] → [(3,3), (5,5), (15,17), (25,25)]
    /// </summary>
    private static List<(int Start, int End)> GroupConsecutivePages(List<int> pages)
    {
        if (pages.Count == 0) return new();

        var sorted = pages.OrderBy(p => p).Distinct().ToList();
        var ranges = new List<(int, int)>();
        int start = sorted[0], end = sorted[0];

        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == end + 1)
            {
                end = sorted[i];
            }
            else
            {
                ranges.Add((start, end));
                start = sorted[i];
                end = sorted[i];
            }
        }
        ranges.Add((start, end));

        return ranges;
    }

    private static ExtractionResponse ErrorResponse(
        string requestId, string docType, string fileName, string code, string message)
    {
        return new ExtractionResponse
        {
            RequestId = requestId,
            DocumentType = docType,
            Status = ExtractionStatus.Failed,
            Metadata = new ExtractionMetadata { SourceFile = fileName },
            Validation = new ValidationSummary { IsValid = false },
            Error = new ErrorDetail { Code = code, Message = message }
        };
    }

    // ── Models ──

    private class WellPageMap
    {
        public string WellName { get; init; } = "";
        public List<int> Pages { get; init; } = new();
    }
}
