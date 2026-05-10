using DocumentExtractionService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocumentExtractionService.Api.Controllers;

/// <summary>
/// Export endpoints for downloading extraction results as JSON or Excel.
///
/// ENDPOINTS:
/// GET /api/v1/export/{requestId}/json   ? Download extraction result as JSON file
/// GET /api/v1/export/{requestId}/excel  ? Download extraction result as Excel file
///
/// Results are looked up from the in-memory result store populated
/// during extraction. Results expire after the store's retention period.
/// </summary>
[ApiController]
[Route("api/v1/export")]
[Authorize]
[EnableRateLimiting("per-client")]
public class ExportController : ControllerBase
{
    private readonly IExtractionResultStore _resultStore;
    private readonly IExportService _exportService;
    private readonly ILogger<ExportController> _logger;

    public ExportController(
        IExtractionResultStore resultStore,
        IExportService exportService,
        ILogger<ExportController> logger)
    {
        _resultStore = resultStore;
        _exportService = exportService;
        _logger = logger;
    }

    /// <summary>
    /// Download extraction result as a formatted JSON file.
    /// </summary>
    [HttpGet("{requestId}/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public IActionResult ExportJson(string requestId)
    {
        return ExportAs(requestId, ExportFormat.Json);
    }

    /// <summary>
    /// Download extraction result as an Excel (.xlsx) file.
    /// </summary>
    [HttpGet("{requestId}/excel")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public IActionResult ExportExcel(string requestId)
    {
        return ExportAs(requestId, ExportFormat.Excel);
    }

    // ?? Private helpers ????????????????????????????????????????????????????

    private IActionResult ExportAs(string requestId, ExportFormat format)
    {
        var result = _resultStore.Get(requestId);
        if (result is null)
        {
            return NotFound(Problem(
                title: "Extraction result not found",
                detail: $"No extraction result with request ID '{requestId}' was found. " +
                        "Results are available for a limited time after extraction. " +
                        "Failed extractions do not produce exportable data.",
                statusCode: 404));
        }

        _logger.LogInformation("[Export] {RequestId} ? {Format}", requestId, format);

        try
        {
            var export = _exportService.Export(result, format);
            return File(export.FileBytes, export.ContentType, export.FileName);
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(Problem(
                title: "Export not possible",
                detail: ex.Message,
                statusCode: 422));
        }
    }
}
