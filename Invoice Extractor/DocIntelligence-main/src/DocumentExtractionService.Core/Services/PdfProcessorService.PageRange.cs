using Docnet.Core;
using Docnet.Core.Models;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;

namespace DocumentExtractionService.Core.Services;

/// <summary>Page-range processing for chunked extraction (full image coverage per chunk).</summary>
public partial class PdfProcessorService
{
    /// <summary>
    /// Process a specific page range from a PDF. Used by <see cref="ChunkedExtractionStrategy"/>
    /// to extract and render only the pages in a given chunk.
    /// </summary>
    public PdfContent ProcessPdfPageRange(
        string filePath, int startPage, int endPage, PdfProcessingOptions? options = null)
    {
        options ??= new PdfProcessingOptions();
        if (options.UseDualTextExtraction)
            return ProcessPdfPageRangeWithDualText(filePath, startPage, endPage, options);
        var fileInfo = new FileInfo(filePath);
        var content = new PdfContent
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSize = fileInfo.Length
        };

        using var docReader = DocLib.Instance.GetDocReader(
            filePath, new PageDimensions(options.ImageMaxWidthPx));

        int totalPages = docReader.GetPageCount();
        content.PageCount = totalPages;

        int start0 = Math.Max(0, startPage - 1);
        int end0 = Math.Min(totalPages - 1, endPage - 1);

        _logger.LogDebug("ProcessPdfPageRange: pages {Start}-{End} (0-indexed: {S0}-{E0}) of {Total} total",
            startPage, endPage, start0, end0, totalPages);

        var rangeText = new System.Text.StringBuilder();
        int pagesWithText = 0;

        for (int i = start0; i <= end0; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            var pageText = pageReader.GetText()?.Trim() ?? "";

            if (pageText.Length > 30)
            {
                pagesWithText++;
                rangeText.AppendLine($"[PAGE {i + 1} of {totalPages}]");
                rangeText.AppendLine(pageText);
                rangeText.AppendLine();
            }
        }

        content.ExtractedText = rangeText.ToString();
        content.IsScanned = pagesWithText == 0;
        content.ExtractionMethod = content.IsScanned ? "vision" : "hybrid";

        content.PageImages = RenderPageRange(filePath, start0, end0, options);

        _logger.LogInformation(
            "  Chunk PDF: pages {Start}-{End}, {TextPages} with text, " +
            "{ImageCount} images rendered, text={TextLen:N0} chars",
            startPage, endPage, pagesWithText,
            content.PageImages.Count, content.ExtractedText.Length);

        return content;
    }

    private List<byte[]> RenderPageRange(
        string filePath, int startPage0, int endPage0, PdfProcessingOptions options)
    {
        var images = new List<byte[]>();

        try
        {
            int targetWidth = (int)(8.5 * options.ImageDpi);
            int targetHeight = (int)(11.0 * options.ImageDpi);

            if (targetWidth > options.ImageMaxWidthPx)
            {
                float scale = (float)options.ImageMaxWidthPx / targetWidth;
                targetWidth = options.ImageMaxWidthPx;
                targetHeight = (int)(targetHeight * scale);
            }

            using var docReader = DocLib.Instance.GetDocReader(
                filePath, new PageDimensions(targetWidth, targetHeight));

            int totalPages = docReader.GetPageCount();
            int end = Math.Min(endPage0, totalPages - 1);

            for (int i = startPage0; i <= end; i++)
            {
                try
                {
                    using var pageReader = docReader.GetPageReader(i);
                    int width = pageReader.GetPageWidth();
                    int height = pageReader.GetPageHeight();
                    var rawBytes = pageReader.GetImage();

                    if (rawBytes == null || rawBytes.Length == 0) continue;

                    var pngBytes = ConvertBgraToPng(rawBytes, width, height);
                    if (pngBytes.Length > 0)
                        images.Add(pngBytes);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "  Failed to render page {Page} in range", i + 1);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render page range {Start}-{End} from {File}",
                startPage0, endPage0, filePath);
        }

        return images;
    }
}
