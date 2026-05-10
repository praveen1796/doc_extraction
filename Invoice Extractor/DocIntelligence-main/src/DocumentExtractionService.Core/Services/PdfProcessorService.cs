using Docnet.Core;
using Docnet.Core.Models;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Options for PDF processing.
/// </summary>
public class PdfProcessingOptions
{
    public int MaxPagesForVision { get; set; } = 12;
    public int ImageDpi { get; set; } = 200;
    public int ImageMaxWidthPx { get; set; } = 2048;

    /// <summary>Max characters of extracted text to include (0 = use <see cref="PdfProcessorService.DefaultMaxTextChars"/>).</summary>
    public int MaxTextChars { get; set; } = 0;

    /// <summary>
    /// When text exceeds the cap: keep only the beginning, or keep beginning + end (exhibits) so late-page rates are not lost.
    /// </summary>
    public TextTruncationMode TextTruncation { get; set; } = TextTruncationMode.HeadOnly;
    public bool UseDualTextExtraction { get; set; } = false;
}

/// <summary>How to apply <see cref="PdfProcessingOptions.MaxTextChars"/> when the full text is longer.</summary>
public enum TextTruncationMode
{
    /// <summary>Keep only the first N characters (legacy).</summary>
    HeadOnly = 0,

    /// <summary>Keep the first ~55% and last ~40% of the cap so pages at the end (e.g. exhibits) remain in the prompt.</summary>
    HeadAndTail = 1
}

/// <summary>
/// Processes PDF files: extracts text and renders pages to images.
/// Ported from the original InvoiceExtractor console app with options pattern.
///
/// FIX v1.2:
/// - Added text truncation to prevent context window overflow on large docs (100+ pages)
/// - Added logging for image sizes to help diagnose token consumption
/// - MaxTextChars = 120,000 (~30K tokens) — leaves room for images + schema + prompt
/// </summary>
public partial class PdfProcessorService
{
    private readonly ILogger<PdfProcessorService> _logger;

    /// <summary>Default cap when document type does not override (see <see cref="PdfProcessingOptions.MaxTextChars"/>).</summary>
    public const int DefaultMaxTextChars = 120_000;

    public PdfProcessorService(ILogger<PdfProcessorService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Process a PDF file with configurable options.
    /// </summary>
    public PdfContent ProcessPdf(string filePath, PdfProcessingOptions? options = null)
    {
        options ??= new PdfProcessingOptions();
        if (options.UseDualTextExtraction)
            return ProcessPdfWithDualText(filePath, options);          // new method
        var fileInfo = new FileInfo(filePath);
        var content = new PdfContent
        {
            FileName = fileInfo.Name,
            FilePath = filePath,
            FileSize = fileInfo.Length
        };

        _logger.LogDebug("Processing PDF: {File} ({Size:N0} bytes)", fileInfo.Name, fileInfo.Length);

        using var docReader = DocLib.Instance.GetDocReader(
            filePath, new PageDimensions(options.ImageMaxWidthPx));

        content.PageCount = docReader.GetPageCount();

        // ── Extract text from all pages ──
        var allText = new System.Text.StringBuilder();
        int pagesWithText = 0;

        for (int i = 0; i < content.PageCount; i++)
        {
            using var pageReader = docReader.GetPageReader(i);
            var pageText = pageReader.GetText()?.Trim() ?? "";

            if (pageText.Length > 30)
            {
                pagesWithText++;
                allText.AppendLine($"[PAGE {i + 1} of {content.PageCount}]");
                allText.AppendLine(pageText);
                allText.AppendLine();
            }
        }

        content.ExtractedText = allText.ToString();
        content.IsScanned = pagesWithText == 0;

        // ══════════════════════════════════════════════════════════════════
        //  FIX v1.2: Truncate extracted text for large documents.
        //
        //  Documents like the 136-page Corral well plan generate 300K+
        //  chars of extracted text. Sending all of it to GPT alongside
        //  page images exceeds the context window and causes:
        //  - HTTP timeouts (model takes too long)
        //  - Context window errors (exceeds model limit)
        //  - Truncated/invalid JSON output
        //
        //  We keep the first N characters (configurable per document type). Page images
        //  supplement content when text is short (scanned PDF) or when truncated.
        // ══════════════════════════════════════════════════════════════════
        int textCap = options.MaxTextChars > 0 ? options.MaxTextChars : DefaultMaxTextChars;
        if (content.ExtractedText.Length > textCap)
        {
            var originalLen = content.ExtractedText.Length;
            content.ExtractedText = options.TextTruncation == TextTruncationMode.HeadAndTail
                ? BuildHeadAndTailText(content.ExtractedText, textCap, content.PageCount, fileInfo.Name)
                : content.ExtractedText[..textCap] +
                  $"\n\n[... TEXT TRUNCATED at {textCap:N0} chars — " +
                  $"original was {originalLen:N0} chars across {content.PageCount} pages. " +
                  "Use page images for data beyond this point. ...]";

            _logger.LogWarning("  Truncated extracted text (mode={Mode}) from {Original:N0} to {Final:N0} chars (cap {Cap:N0}) " +
                "for {File} ({Pages} pages)",
                options.TextTruncation, originalLen, content.ExtractedText.Length, textCap, fileInfo.Name, content.PageCount);
        }

        int pagesToRender = Math.Min(content.PageCount, options.MaxPagesForVision);
        content.ExtractionMethod = content.IsScanned ? "vision" : "hybrid";

        _logger.LogInformation("  PDF: {Pages} total pages, {TextPages} with text, " +
            "rendering {RenderPages} as images, method={Method}, text={TextLen:N0} chars",
            content.PageCount, pagesWithText, pagesToRender,
            content.ExtractionMethod, content.ExtractedText.Length);

        // ── Render pages as images ──
        content.PageImages = RenderPages(filePath, pagesToRender, options);

        // Log image sizes for debugging token consumption
        if (content.PageImages.Count > 0)
        {
            var totalImageBytes = content.PageImages.Sum(img => (long)img.Length);
            _logger.LogDebug("  Rendered {Count} page images, total {Size:N0} bytes (~{SizeKb:N0} KB)",
                content.PageImages.Count, totalImageBytes, totalImageBytes / 1024);
        }

        return content;
    }

    /// <summary>
    /// Keeps the start and end of long PDF text so late exhibits (where many rates live) stay in the model
    /// context. Head-only truncation often leaves only the first ~10–15 dense front pages; the model then
    /// has no <c>[PAGE N]</c> lines for later text and may echo the last page number it saw (e.g. 11).
    /// </summary>
    private string BuildHeadAndTailText(string full, int maxChars, int pageCount, string fileLabel)
    {
        const string middle =
            "\n\n<<< MIDDLE OMITTED — TAIL = later pages (exhibits, schedules). Use images for gaps. >>>\n\n";
        if (full.Length <= maxChars) return full;

        const int footer = 200;
        int budget = maxChars - middle.Length - footer;
        if (budget < 6_000)
        {
            return full[..maxChars] +
                $"\n\n[... TEXT TRUNCATED at {maxChars:N0} of {full.Length:N0} chars, {fileLabel} ...]";
        }

        int headSize = (int)(budget * 0.55);
        int tailSize = budget - headSize;

        // Head: end on a line break for cleaner cuts
        int headEnd = Math.Min(headSize, full.Length);
        for (int i = 0; i < 2_000 && headEnd > 0; i++)
        {
            if (headEnd >= full.Length) { headEnd = full.Length; break; }
            if (full[headEnd - 1] == '\n') break;
            headEnd--;
        }

        if (headEnd + tailSize >= full.Length)
        {
            // Short doc — fall back
            return full[..Math.Min(maxChars, full.Length)];
        }

        // Tail: start at a [PAGE marker if possible (cleaner page boundaries for page_ref)
        int tailStart = full.Length - tailSize;
        int pmark = full.IndexOf("\n[PAGE", tailStart, StringComparison.Ordinal);
        if (pmark < 0)
            pmark = full.IndexOf("\n[PAGE", Math.Max(0, tailStart - 6_000), StringComparison.Ordinal);
        if (pmark >= 0 && pmark + 1 < full.Length) tailStart = pmark + 1;
        if (tailStart <= headEnd) tailStart = headEnd + 1;
        if (tailStart >= full.Length) return full[..maxChars] + " [...]\n";

        var combined = full[..headEnd] + middle + full[tailStart..];
        if (combined.Length > maxChars) combined = combined[..maxChars];

        return combined + $"\n[text: {full.Length} chars, {pageCount} pages, head+tail, {fileLabel}]\n";
    }

    private List<byte[]> RenderPages(string filePath, int pageCount, PdfProcessingOptions options)
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

            int pages = Math.Min(docReader.GetPageCount(), pageCount);

            for (int i = 0; i < pages; i++)
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
                    _logger.LogWarning(ex, "  Failed to render page {Page}", i + 1);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render pages from {File}", filePath);
        }

        return images;
    }

    private byte[] ConvertBgraToPng(byte[] bgraData, int width, int height)
    {
        try
        {
#pragma warning disable CA1416 // System.Drawing GDI+ — same Windows deployment as original service
            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);

            int dataLength = Math.Min(bgraData.Length, bitmapData.Stride * height);
            Marshal.Copy(bgraData, 0, bitmapData.Scan0, dataLength);
            bitmap.UnlockBits(bitmapData);

            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
#pragma warning restore CA1416
            var pngBytes = ms.ToArray();

            if (pngBytes.Length < 8 || pngBytes[0] != 0x89 || pngBytes[1] != 0x50)
                return Array.Empty<byte>();

            return pngBytes;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PNG conversion failed for {W}x{H}. On Linux, install libgdiplus.", width, height);
            return Array.Empty<byte>();
        }
    }
}
