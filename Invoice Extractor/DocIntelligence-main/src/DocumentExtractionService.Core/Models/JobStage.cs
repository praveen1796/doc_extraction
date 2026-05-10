using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

/// <summary>
/// Pipeline stage for async job progress tracking.
/// Represents the current step in the extraction pipeline
/// so the UI can show meaningful progress indicators.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum JobStage
{
    /// <summary>Job accepted, files queued for processing.</summary>
    Uploaded,

    /// <summary>Document type classification / validation in progress.</summary>
    Classified,

    /// <summary>PDF pages being read and rendered to images.</summary>
    ReadingPages,

    /// <summary>GPT extraction (primary pass) in progress.</summary>
    ExtractingFields,

    /// <summary>Validation and optional dual-pass verification.</summary>
    Validating,

    /// <summary>Preparing export / finalizing results.</summary>
    PreparingExport,

    /// <summary>All processing complete, results available.</summary>
    Completed,

    /// <summary>Processing failed with an error.</summary>
    Failed
}
