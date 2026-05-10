using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DocumentExtractionService.Api.Controllers;

/// <summary>
/// MVP: conversational Q+A over extracted JSON only (no raw PDF).
/// </summary>
[ApiController]
[Route("api/v1/extraction")]
[Authorize]
[EnableRateLimiting("per-client")]
public class ExtractionChatController : ControllerBase
{
    private readonly IExtractionChatService _chat;
    private readonly ILogger<ExtractionChatController> _logger;

    public ExtractionChatController(IExtractionChatService chat, ILogger<ExtractionChatController> logger)
    {
        _chat = chat;
        _logger = logger;
    }

    /// <summary>
    /// Ask a question about a completed extraction. Use <c>request_id</c> after POST extract, or send <c>data</c> for inline JSON (e.g. demo UI).
    /// </summary>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(ExtractionChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExtractionChatResponse>> Chat(
        [FromBody] ExtractionChatRequest body,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _chat.ChatAsync(body, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(Problem(title: "Invalid chat request", detail: ex.Message, statusCode: 400));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(Problem(title: "Extraction not found", detail: ex.Message, statusCode: 404));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat failed");
            return Problem(title: "Chat failed", detail: ex.Message, statusCode: 502);
        }
    }
}
