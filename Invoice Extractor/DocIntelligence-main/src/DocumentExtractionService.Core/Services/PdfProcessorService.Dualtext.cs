using System.Text;
using System.Text.RegularExpressions;
using Docnet.Core;
using Docnet.Core.Models;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;
using Tesseract;
using UglyToad.PdfPig;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// INVOICE-ONLY dual text extraction.
///
/// Runs BOTH PdfPig (digital text with column positions) AND Tesseract OCR
/// (visible-pixel text) per page, then reconciles per page:
///   - Both blank          → skip page (pure vision)
///   - One blank           → use the non-blank one
///   - Both agree (≥85%)   → send PdfPig (better column alignment)
///   - Both differ         → send BOTH with a DISAGREEMENT flag, vision tiebreaker
///
/// Activated only when <see cref="PdfProcessingOptions.UseDualTextExtraction"/> is true.
/// Wired in by GenericExtractionService / ChunkedExtractionStrategy when
/// docTypeConfig.Id == "invoice".
/// </summary>
public partial class PdfProcessorService
{
    // ════════════════════════════════════════════════════════════════
    //  PUBLIC ENTRY POINTS (called from ProcessPdf / ProcessPdfPageRange
    //  when options.UseDualTextExtraction == true)
    // ════════════════════════════════════════════════════════════════

    internal PdfContent ProcessPdfWithDualText(string filePath, PdfProcessingOptions options)
    {
        var fileInfo = new FileInfo(filePath);
        var content = new PdfContent
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSize = fileInfo.Length
        };

        _logger.LogInformation("[Invoice/DualText] Processing PDF: {File} ({Size:N0} bytes)",
            fileInfo.Name, fileInfo.Length);

        using (var docReader = DocLib.Instance.GetDocReader(
            filePath, new PageDimensions(options.ImageMaxWidthPx)))
        {
            content.PageCount = docReader.GetPageCount();
        }

        // ── Step 1: Render images (needed for vision + Tesseract) ──
        int pagesToRender = Math.Min(content.PageCount, options.MaxPagesForVision);
        content.PageImages = RenderPages(filePath, pagesToRender, options);

        // ── Step 2: Run BOTH text engines ──
        var pdfPigText = ExtractTextWithPdfPig(filePath, content.PageCount, pagesToRender);
        var tesseractText = ExtractTextWithTesseractOcr(content.PageImages, content.PageCount);

        _logger.LogInformation("  PdfPig: {PigLen:N0} chars | Tesseract: {TessLen:N0} chars",
            pdfPigText.Length, tesseractText.Length);

        // ── Step 3: Reconcile per page ──
        content.ExtractedText = MergeAndFlagExtractions(pdfPigText, tesseractText, content.PageCount);

        int pagesWithText = content.ExtractedText
            .Split("[PAGE ", StringSplitOptions.RemoveEmptyEntries)
            .Count(p => p.Length > 10);

        content.IsScanned = pagesWithText == 0;
        content.ExtractionMethod = content.IsScanned ? "vision" : "hybrid-dual-text";

        _logger.LogInformation(
            "  [Invoice/DualText] {TextLen:N0} chars text, {ImgCount} page images, method={Method}",
            content.ExtractedText.Length, content.PageImages.Count, content.ExtractionMethod);

        return content;
    }

    internal PdfContent ProcessPdfPageRangeWithDualText(
        string filePath, int startPage, int endPage, PdfProcessingOptions options)
    {
        var fileInfo = new FileInfo(filePath);
        var content = new PdfContent
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSize = fileInfo.Length
        };

        int totalPages;
        using (var docReader = DocLib.Instance.GetDocReader(
            filePath, new PageDimensions(options.ImageMaxWidthPx)))
        {
            totalPages = docReader.GetPageCount();
        }
        content.PageCount = totalPages;

        int start1 = Math.Max(1, startPage);
        int end1 = Math.Min(totalPages, endPage);

        _logger.LogInformation("[Invoice/DualText] Range pages {Start}-{End} of {Total}",
            start1, end1, totalPages);

        // Render only the page range
        content.PageImages = RenderPageRange(filePath, start1 - 1, end1 - 1, options);

        // PdfPig: extract just this range
        var pdfPigText = ExtractTextWithPdfPigRange(filePath, totalPages, start1, end1);
        // Tesseract: re-key images to their absolute page numbers
        var tesseractText = ExtractTextWithTesseractOcrRange(content.PageImages, totalPages, start1);

        content.ExtractedText = MergeAndFlagExtractions(pdfPigText, tesseractText, totalPages);

        int pagesWithText = content.ExtractedText
            .Split("[PAGE ", StringSplitOptions.RemoveEmptyEntries)
            .Count(p => p.Length > 10);

        content.IsScanned = pagesWithText == 0;
        content.ExtractionMethod = content.IsScanned ? "vision" : "hybrid-dual-text";

        _logger.LogInformation(
            "  [Invoice/DualText] Range {Start}-{End}: {TextLen:N0} chars, {ImgCount} images, method={Method}",
            start1, end1, content.ExtractedText.Length, content.PageImages.Count, content.ExtractionMethod);

        return content;
    }

    // ════════════════════════════════════════════════════════════════
    //  RECONCILIATION
    // ════════════════════════════════════════════════════════════════

    private string MergeAndFlagExtractions(string pdfPigText, string tesseractText, int pageCount)
    {
        var pdfPigPages = SplitByPages(pdfPigText);
        var tesseractPages = SplitByPages(tesseractText);
        var merged = new StringBuilder();

        int agreeCount = 0, pigOnlyCount = 0, tessOnlyCount = 0, disagreeCount = 0, blankCount = 0;

        for (int i = 1; i <= pageCount; i++)
        {
            pdfPigPages.TryGetValue(i, out var pigPage);
            tesseractPages.TryGetValue(i, out var tessPage);

            pigPage = pigPage?.Trim() ?? "";
            tessPage = tessPage?.Trim() ?? "";

            bool pigBlank = string.IsNullOrWhiteSpace(pigPage);
            bool tessBlank = string.IsNullOrWhiteSpace(tessPage);

            if (pigBlank && tessBlank)
            {
                blankCount++;
                _logger.LogDebug("  Page {Page}: both extractors blank — vision only", i);
                continue;
            }

            merged.AppendLine($"[PAGE {i} of {pageCount}]");

            if (pigBlank)
            {
                tessOnlyCount++;
                merged.AppendLine("[SOURCE: Tesseract OCR only — PdfPig found no text on this page]");
                merged.AppendLine(tessPage);
            }
            else if (tessBlank)
            {
                pigOnlyCount++;
                merged.AppendLine("[SOURCE: PdfPig only — Tesseract found no text on this page]");
                merged.AppendLine(pigPage);
            }
            else if (TextsAgree(pigPage, tessPage))
            {
                agreeCount++;
                merged.AppendLine("[SOURCE: PdfPig and Tesseract agree]");
                merged.AppendLine(pigPage);
            }
            else
            {
                disagreeCount++;
                merged.AppendLine("[SOURCE: PdfPig and Tesseract DISAGREE — verify against page image, use vision as tiebreaker]");
                merged.AppendLine("--- PdfPig text (preserves exact column positions; may include barcodes/hidden text) ---");
                merged.AppendLine(pigPage);
                merged.AppendLine("--- Tesseract OCR text (may have character errors but reads visible content) ---");
                merged.AppendLine(tessPage);
                _logger.LogWarning("  Page {Page}: PdfPig vs Tesseract DISAGREE — vision tiebreaker required", i);
            }

            merged.AppendLine();
        }

        _logger.LogInformation(
            "  Reconciliation: agree={Agree}, pdfpig-only={Pig}, tesseract-only={Tess}, disagree={Dis}, blank={Blank}",
            agreeCount, pigOnlyCount, tessOnlyCount, disagreeCount, blankCount);

        return merged.ToString();
    }

    private static bool TextsAgree(string a, string b)
    {
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);
        if (tokensA.Count == 0 || tokensB.Count == 0) return false;

        int intersection = tokensA.Intersect(tokensB).Count();
        int union = tokensA.Union(tokensB).Count();
        return (double)intersection / union >= 0.85;
    }

    private static HashSet<string> Tokenize(string text) =>
        Regex.Matches(text.ToLowerInvariant(), @"[a-z0-9]{2,}")
             .Select(m => m.Value)
             .ToHashSet();

    private static Dictionary<int, string> SplitByPages(string text)
    {
        var pages = new Dictionary<int, string>();
        if (string.IsNullOrWhiteSpace(text)) return pages;

        var matches = Regex.Matches(
            text,
            @"\[PAGE (\d+) of \d+\]\s*\n(.*?)(?=\[PAGE \d+ of \d+\]|$)",
            RegexOptions.Singleline);

        foreach (Match match in matches)
        {
            if (int.TryParse(match.Groups[1].Value, out int pageNum))
                pages[pageNum] = match.Groups[2].Value.Trim();
        }
        return pages;
    }

    // ════════════════════════════════════════════════════════════════
    //  PdfPig — digital text extraction with line grouping
    // ════════════════════════════════════════════════════════════════

    private string ExtractTextWithPdfPig(string filePath, int pageCount, int maxPages)
        => ExtractTextWithPdfPigRange(filePath, pageCount, 1, Math.Min(pageCount, maxPages));

    private string ExtractTextWithPdfPigRange(
        string filePath, int totalPages, int startPage1, int endPage1)
    {
        var allText = new StringBuilder();

        try
        {
            using var document = PdfDocument.Open(filePath);

            int start = Math.Max(1, startPage1);
            int end = Math.Min(document.NumberOfPages, endPage1);

            for (int i = start; i <= end; i++)
            {
                try
                {
                    var page = document.GetPage(i);
                    var words = page.GetWords().ToList();
                    if (words.Count == 0) continue;

                    // Group words into visual lines (Y bucket ≈ 5pt). PDF Y origin is at bottom,
                    // so descending Y = top-to-bottom reading order.
                    const double yTolerance = 5.0;

                    var lines = words
                        .GroupBy(w => Math.Round(w.BoundingBox.Bottom / yTolerance) * yTolerance)
                        .OrderByDescending(g => g.Key)
                        .Select(g => string.Join(" ",
                            g.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));

                    var pageText = string.Join(Environment.NewLine, lines);

                    if (pageText.Length > 30)
                    {
                        allText.AppendLine($"[PAGE {i} of {totalPages}]");
                        allText.AppendLine(pageText);
                        allText.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "  PdfPig failed on page {Page}", i);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PdfPig extraction failed for {File}", filePath);
        }

        return allText.ToString();
    }

    // ════════════════════════════════════════════════════════════════
    //  Tesseract OCR
    // ════════════════════════════════════════════════════════════════

    private string ExtractTextWithTesseractOcr(List<byte[]> pageImages, int pageCount)
        => ExtractTextWithTesseractOcrRange(pageImages, pageCount, firstAbsolutePage: 1);

    private string ExtractTextWithTesseractOcrRange(
        List<byte[]> pageImages, int totalPages, int firstAbsolutePage)
    {
        var allText = new StringBuilder();

        var tessDataPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata");
        if (!Directory.Exists(tessDataPath))
        {
            _logger.LogWarning(
                "Tesseract tessdata folder not found at {Path}, skipping OCR. " +
                "Add 'tessdata\\eng.traineddata' to the output folder to enable invoice OCR.",
                tessDataPath);
            return "";
        }

        try
        {
            using var engine = new TesseractEngine(tessDataPath, "eng", EngineMode.Default);

            for (int i = 0; i < pageImages.Count; i++)
            {
                int absolutePage = firstAbsolutePage + i;
                try
                {
                    using var pix = Pix.LoadFromMemory(pageImages[i]);
                    using var page = engine.Process(pix);

                    var pageText = page.GetText()?.Trim() ?? "";
                    float confidence = page.GetMeanConfidence();

                    _logger.LogDebug("  Tesseract page {Page}: {Confidence:F1}% confidence, {Len} chars",
                        absolutePage, confidence * 100, pageText.Length);

                    if (pageText.Length > 30)
                    {
                        allText.AppendLine($"[PAGE {absolutePage} of {totalPages}]");
                        allText.AppendLine(pageText);
                        allText.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "  Tesseract OCR failed on page {Page}", absolutePage);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tesseract OCR engine initialization failed");
        }

        return allText.ToString();
    }
}
