using System.Diagnostics;

namespace DocumentExtractionService.Api.Middleware;

/// <summary>
/// Request/response logging middleware.
/// Logs all API calls with timing, client info, and correlation IDs.
/// Used for audit trail and performance monitoring.
/// </summary>
public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = context.TraceIdentifier;

        // Add correlation ID to response headers
        context.Response.Headers["X-Correlation-Id"] = correlationId;
        context.Response.Headers["X-Request-Id"] = correlationId;

        try
        {
            await _next(context);
        }
        finally
        {
            sw.Stop();

            var level = context.Response.StatusCode >= 500
                ? LogLevel.Error
                : context.Response.StatusCode >= 400
                    ? LogLevel.Warning
                    : LogLevel.Information;

            _logger.Log(level,
                "[{CorrelationId}] {Method} {Path} → {StatusCode} in {ElapsedMs}ms | IP={IP} | User={User}",
                correlationId,
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                sw.ElapsedMilliseconds,
                context.Connection.RemoteIpAddress,
                context.User.Identity?.Name ?? "anonymous");
        }
    }
}
