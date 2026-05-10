using DocumentExtractionService.Api.Services;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
//using DocumentExtractionService.Api.Services;



namespace DocumentExtractionService.Api.Controllers;

/// <summary>
/// Document Extraction API Controller.
///
/// ENDPOINTS:
/// POST /api/v1/extraction/extract         → Extract single document
/// POST /api/v1/extraction/batch           → Extract multiple documents
/// GET  /api/v1/extraction/jobs/{jobId}    → Check async job status
/// </summary>
[ApiController]
[Route("api/v1/extraction")]
[Authorize]
[EnableRateLimiting("per-client")]


public class ExtractionController : ControllerBase
{
    private readonly IDocumentExtractionService _extractionService;
    private readonly IDocumentTypeRegistry _registry;
    private readonly IJobTrackingService _jobTracking;
    private readonly IJobProgressReporter _progress;
    private readonly ILogger<ExtractionController> _logger;
    private readonly IApproveQueue _approveQueue;


    public ExtractionController(
        IDocumentExtractionService extractionService,
        IDocumentTypeRegistry registry,
        IJobTrackingService jobTracking,
        IJobProgressReporter progress,
        ILogger<ExtractionController> logger,
        IApproveQueue approveQueue)
    {
        _extractionService = extractionService;
        _registry = registry;
        _jobTracking = jobTracking;
        _progress = progress;
        _logger = logger;
        _approveQueue = approveQueue;
    }


    /// <summary>
    /// Extract data from a single document.
    ///
    /// REQUEST (multipart/form-data):
    ///   file         → Document file (PDF, image, etc.)
    ///   documentType → Document type key (e.g., "invoice", "purchase_order")
    ///   options      → Optional JSON string with ExtractionOptions
    ///
    /// RESPONSE: ExtractionResponse with extracted data as JSON
    /// </summary>
    [HttpPost("extract")]
    [RequestSizeLimit(52_428_800)] // 50 MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ExtractionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ExtractionResponse>> Extract(
        [FromForm] IFormFile file,
        [FromForm] string documentType = "invoice",
        [FromForm] string? optionsJson = null,
        CancellationToken cancellationToken = default)
    {
        // ── Validate input ──
        if (file == null || file.Length == 0)
        {
            return BadRequest(Problem(
                title: "No file provided",
                detail: "Upload a document file using the 'file' form field.",
                statusCode: 400));
        }

        if (!_registry.IsValidDocumentType(documentType))
        {
            var available = string.Join(", ", _registry.GetAllDocumentTypes().Select(t => t.Id));
            return BadRequest(Problem(
                title: "Invalid document type",
                detail: $"Document type '{documentType}' is not available. Available types: {available}",
                statusCode: 400));
        }

        // Parse optional options
        ExtractionOptions? options = null;
        if (!string.IsNullOrEmpty(optionsJson))
        {
            try
            {
                options = System.Text.Json.JsonSerializer.Deserialize<ExtractionOptions>(optionsJson);
            }
            catch (Exception ex)
            {
                return BadRequest(Problem(
                    title: "Invalid options",
                    detail: $"Could not parse options JSON: {ex.Message}",
                    statusCode: 400));
            }
        }

        var requestId = GenerateRequestId();
        var clientId = GetClientId();

        _logger.LogInformation("[{RequestId}] Extract: {File} ({Size:N0} bytes) type={DocType} client={ClientId}",
            requestId, file.FileName, file.Length, documentType, clientId);

        // ── Extract ──
        await using var stream = file.OpenReadStream();
        var result = await _extractionService.ExtractAsync(
            stream, file.FileName, documentType, options, requestId, cancellationToken);

        var statusCode = result.Status switch
        {
            ExtractionStatus.Failed => StatusCodes.Status422UnprocessableEntity,
            ExtractionStatus.PartialSuccess => StatusCodes.Status207MultiStatus,
            _ => StatusCodes.Status200OK
        };

        return StatusCode(statusCode, result);
    }

    /// <summary>
    /// Extract data from multiple documents in a single request.
    ///
    /// REQUEST (multipart/form-data):
    ///   files[]           → Document files
    ///   defaultDocType    → Default document type for all files
    ///   fileTypeOverrides → JSON mapping of filename → documentType
    ///   async             → If true, returns job ID for polling (default: false)
    ///   options           → Optional ExtractionOptions JSON
    ///
    /// RESPONSE (sync): BatchExtractionResponse with all results
    /// RESPONSE (async): BatchExtractionResponse with jobId for polling
    /// </summary>
    [HttpPost("batch")]
    [RequestSizeLimit(524_288_000)] // 500 MB
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(BatchExtractionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(BatchExtractionResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BatchExtractionResponse>> Batch(
        [FromForm] IFormFileCollection files,
        [FromForm] string defaultDocType = "invoice",
        [FromForm] string? fileTypeOverridesJson = null,
        [FromForm] bool async = false,
        [FromForm] string? optionsJson = null,
        CancellationToken cancellationToken = default)
    {
        if (files == null || files.Count == 0)
        {
            return BadRequest(Problem(
                title: "No files provided",
                detail: "Upload one or more files using the 'files' form field.",
                statusCode: 400));
        }

        // Parse options
        ExtractionOptions? options = null;
        Dictionary<string, string> fileTypeOverrides = new();

        if (!string.IsNullOrEmpty(optionsJson))
        {
            try { options = System.Text.Json.JsonSerializer.Deserialize<ExtractionOptions>(optionsJson); }
            catch { /* ignore, use defaults */ }
        }

        if (!string.IsNullOrEmpty(fileTypeOverridesJson))
        {
            try
            {
                fileTypeOverrides = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                    fileTypeOverridesJson) ?? new();
            }
            catch { /* ignore */ }
        }

        var clientId = GetClientId();
        _logger.LogInformation("[Batch] {Count} files, type={DocType}, async={Async}, client={ClientId}",
            files.Count, defaultDocType, async, clientId);

        if (async)
        {
            // Queue for async processing
            var job = _jobTracking.CreateJob(files.Count, clientId, defaultDocType);
            _ = ProcessBatchAsync(job.JobId, files, defaultDocType, fileTypeOverrides, options);

            return Accepted(new BatchExtractionResponse
            {
                BatchId = job.JobId,
                Status = ExtractionStatus.Queued,
                Total = files.Count,
                JobId = job.JobId,
                PollUrl = Url.Action("GetJobStatus", new { jobId = job.JobId })
            });
        }

        // Synchronous processing
        var documents = files.Select(f =>
        {
            var docType = fileTypeOverrides.TryGetValue(f.FileName, out var t) ? t : defaultDocType;
            return (f.OpenReadStream(), f.FileName, (string?)docType);
        }).ToList();

        var result = await _extractionService.ExtractBatchAsync(documents, options, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Get status and results of an async batch job.
    /// </summary>
    [HttpGet("jobs/{jobId}")]
    [ProducesResponseType(typeof(ExtractionJob), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<ExtractionJob> GetJobStatus(string jobId)
    {
        var job = _jobTracking.GetJob(jobId);
        if (job == null)
        {
            return NotFound(Problem(
                title: "Job not found",
                detail: $"No job with ID '{jobId}' found. Jobs expire after 24 hours.",
                statusCode: 404));
        }

        // Only return jobs belonging to this client
        var clientId = GetClientId();
        if (job.ClientId != clientId)
        {
            return NotFound();
        }

        return Ok(job);
    }
    /// <summary>
    ///  Approve
    /// Used to verify UI → backend wiring.
    /// </summary>
    [HttpPost("approve")]
    [Consumes("application/json")]
    public IActionResult Approve([FromBody] ApproveRequest request)
    {
        _logger.LogInformation(
            "Approve hit. RequestId={RequestId}, HasEdits={HasEdits}",
            request.RequestId,
            request.HasEdits
        );

      
        if (request.Data != null)
        {
            _approveQueue.Enqueue(request.RequestId, request.Data);
        }

        return Ok(new
        {
            status = "ok",
            message = "Approve successful"
        });
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private async Task ProcessBatchAsync(
        string jobId,
        IFormFileCollection files,
        string defaultDocType,
        Dictionary<string, string> fileTypeOverrides,
        ExtractionOptions? options)
    {
        try
        {
            _progress.ReportStage(jobId, JobStage.ReadingPages,
                "Copying files for processing...");

            var documents = new List<(Stream, string, string?)>();

            foreach (var file in files)
            {
                var ms = new MemoryStream();
                await file.CopyToAsync(ms);
                ms.Position = 0;
                var docType = fileTypeOverrides.TryGetValue(file.FileName, out var t) ? t : defaultDocType;
                documents.Add((ms, file.FileName, docType));
            }

            _progress.ReportStage(jobId, JobStage.ExtractingFields,
                $"Extracting data from {documents.Count} document(s)...");

            var result = await _extractionService.ExtractBatchAsync(documents);

            _progress.ReportCompletion(jobId, result);
        }
        catch (Exception ex)
        {
            _progress.ReportFailure(jobId, ex.Message, ex);
        }
    }

    private string GetClientId()
    {
        return User.FindFirst("client_id")?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "anonymous";
    }

    private static string GenerateRequestId()
        => DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N")[..8];
}

public sealed class ApproveRequest
{
    public string RequestId { get; set; } = "";
    public bool HasEdits { get; set; }
    public object? Data { get; set; }
}

