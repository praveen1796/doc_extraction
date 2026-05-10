using System.Net;
using System.Text.Json;

namespace DocumentExtractionService.Api.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Converts all unhandled exceptions to consistent ProblemDetails responses.
/// Includes correlation ID in all error responses for support traceability.
///
/// FIX v1.2: Added TaskCanceledException handling for HTTP-level timeouts.
/// When Azure OpenAI HttpClient times out, it throws TaskCanceledException
/// (not TimeoutException). Previously this fell through to the generic 500
/// handler, giving unhelpful error messages.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — not an error
            _logger.LogDebug("Client disconnected for {Method} {Path}",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode = 499; // Client Closed Request
        }
        catch (TaskCanceledException ex) when (!context.RequestAborted.IsCancellationRequested)
        {
            // ══════════════════════════════════════════════════════════════
            //  FIX v1.2: HTTP-level timeout (not client disconnect).
            //  This happens when Azure OpenAI takes too long to respond.
            //  Return 504 Gateway Timeout with helpful message.
            // ══════════════════════════════════════════════════════════════
            _logger.LogWarning(ex,
                "HTTP timeout for request {Method} {Path} [{CorrelationId}]",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);

            await WriteErrorResponse(context, HttpStatusCode.GatewayTimeout,
                "Extraction timeout",
                "The AI model took too long to respond. This can happen with large documents " +
                "(100+ pages) or complex well plans. The document was likely too large for " +
                "a single extraction pass. Try reducing the number of pages or using the " +
                "async batch endpoint for large documents.");
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.TraceIdentifier;

        _logger.LogError(exception,
            "Unhandled exception for request {Method} {Path} [{CorrelationId}]",
            context.Request.Method,
            context.Request.Path,
            correlationId);

        var (statusCode, title, detail) = exception switch
        {
            ArgumentException ex => (HttpStatusCode.BadRequest, "Invalid argument", ex.Message),
            InvalidOperationException ex => (HttpStatusCode.UnprocessableEntity, "Processing error", ex.Message),
            FileNotFoundException ex => (HttpStatusCode.NotFound, "Resource not found", ex.Message),
            NotSupportedException ex => (HttpStatusCode.BadRequest, "Operation not supported", ex.Message),
            TimeoutException => (HttpStatusCode.GatewayTimeout, "Request timeout",
                "The extraction timed out. Try reducing file size or number of pages."),
            _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred",
                "Please try again. If the problem persists, contact support with correlation ID: " + correlationId)
        };

        await WriteErrorResponse(context, statusCode, title, detail);
    }

    private static async Task WriteErrorResponse(
        HttpContext context,
        HttpStatusCode statusCode,
        string title,
        string detail)
    {
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new
        {
            type = $"https://httpstatuses.com/{(int)statusCode}",
            title,
            status = (int)statusCode,
            detail,
            instance = context.Request.Path.Value,
            correlationId = context.TraceIdentifier,
            timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
