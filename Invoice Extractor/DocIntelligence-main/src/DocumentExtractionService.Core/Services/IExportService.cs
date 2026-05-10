using DocumentExtractionService.Core.Models;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Export format identifier.
/// Extensible — add CSV, PDF, etc. as needed.
/// </summary>
public enum ExportFormat
{
    Json,
    Excel
}

/// <summary>
/// Result of an export operation.
/// Contains the file bytes, content type, and suggested filename.
/// </summary>
public class ExportResult
{
    public required byte[] FileBytes { get; init; }
    public required string ContentType { get; init; }
    public required string FileName { get; init; }
}

/// <summary>
/// Abstraction for generating export files from extraction results.
/// Keeps export logic out of controllers and allows format extensibility.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Generate an export file from a completed extraction result.
    /// Throws <see cref="InvalidOperationException"/> when the result has no exportable data.
    /// Throws <see cref="NotSupportedException"/> for unsupported formats.
    /// </summary>
    ExportResult Export(ExtractionResponse result, ExportFormat format);
}
