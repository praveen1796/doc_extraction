
using DocumentExtractionService.Api.Auth;
using DocumentExtractionService.Api.Middleware;
using DocumentExtractionService.Api.Services;
using DocumentExtractionService.Core.Configuration;
using DocumentExtractionService.Core.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web;
using Serilog;
using Serilog.Events;
using System.Threading.RateLimiting;

// ═══════════════════════════════════════════════════════════════════════════════
//  GENERIC DOCUMENT EXTRACTION SERVICE
//  Built on ASP.NET Core 8 | Azure OpenAI | Plugin-based Document Type Registry
// ═══════════════════════════════════════════════════════════════════════════════

var builder = WebApplication.CreateBuilder(args);

// ══════════════════════════════════════════════════════════════════════════════
//  FIX v1.2: Configure Kestrel timeouts for long-running extraction requests.
//
//  Well plan extraction can take 3-10 minutes for large multi-well documents.
//  Default Kestrel timeouts will kill the connection before GPT responds.
// ══════════════════════════════════════════════════════════════════════════════
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(15);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(15);
});

// ── Configuration ──────────────────────────────────────────────────────────────
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables(prefix: "DOCEXTRACT_")
    .AddCommandLine(args);

// Bind AppSettings
var appSettings = new AppSettings();
builder.Configuration.Bind(appSettings);
builder.Services.Configure<AppSettings>(builder.Configuration);
builder.Services.AddSingleton(appSettings);

builder.Services.AddSingleton<IApproveQueue, ApproveQueue>();
builder.Services.AddHostedService<ApproveBlobWorker>();


// ── Serilog Logging ────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.WithProperty("Service", "DocExtractionService")
    .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Host.UseSerilog();

// ── Authentication ─────────────────────────────────────────────────────────────
if (appSettings.Auth.Enabled)
{
    // Determine the default scheme based on what is enabled
    var useAzureAd = appSettings.Auth.AzureAd.Enabled && !string.IsNullOrEmpty(appSettings.Auth.AzureAd.TenantId);
    var useApiKey = appSettings.Auth.ApiKey.Enabled;
    var defaultScheme = useAzureAd
        ? JwtBearerDefaults.AuthenticationScheme
        : useApiKey
            ? ApiKeyAuthenticationOptions.DefaultScheme
            : JwtBearerDefaults.AuthenticationScheme;

    var authBuilder = builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = defaultScheme;
        options.DefaultChallengeScheme = defaultScheme;
    });

    // Azure AD JWT Bearer (primary)
    if (useAzureAd)
    {
        authBuilder.AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("Auth:AzureAd"));
        Log.Information("Azure AD authentication enabled (Tenant: {TenantId})",
            appSettings.Auth.AzureAd.TenantId);
    }

    // API Key (fallback / service-to-service)
    if (useApiKey)
    {
        authBuilder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.DefaultScheme, _ => { });
        Log.Information("API Key authentication enabled ({Count} configured keys)",
            appSettings.Auth.ApiKey.Keys.Count);
    }

    // Authorization policies
    builder.Services.AddAuthorization(options =>
    {
        // Any authenticated user (JWT or API key)
        options.AddPolicy("Authenticated", policy =>
            policy.RequireAuthenticatedUser());

        // Admin-only endpoints (document type management)
        options.AddPolicy("AdminOnly", policy =>
            policy.RequireAuthenticatedUser()
                  .RequireRole(appSettings.Auth.AzureAd.AdminRoles.ToArray()));

        // Default policy
        options.DefaultPolicy = options.GetPolicy("Authenticated")!;
        options.FallbackPolicy = options.GetPolicy("Authenticated")!;
    });
}
else
{
    // Auth disabled (development mode)
    Log.Warning("⚠️ Authentication is DISABLED. Set Auth:Enabled=true for production.");
    builder.Services.AddAuthentication();
    builder.Services.AddAuthorization(options =>
    {
        options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true).Build();
        options.FallbackPolicy = options.DefaultPolicy;
        options.AddPolicy("AdminOnly", options.DefaultPolicy);
    });
}

// ── Rate Limiting ──────────────────────────────────────────────────────────────
if (appSettings.RateLimit.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        // Per-client sliding window limiter
        options.AddPolicy("per-client", context =>
        {
            var clientId = context.User.FindFirst("client_id")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            return RateLimitPartition.GetSlidingWindowLimiter(clientId, _ =>
                new SlidingWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromSeconds(appSettings.RateLimit.WindowSeconds),
                    PermitLimit = appSettings.RateLimit.RequestsPerWindow,
                    SegmentsPerWindow = 6,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = appSettings.RateLimit.QueueRequests
                        ? appSettings.RateLimit.MaxQueueDepth : 0
                });
        });

        // Per-client concurrency limiter
        options.AddPolicy("concurrency", context =>
        {
            var clientId = context.User.FindFirst("client_id")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            return RateLimitPartition.GetConcurrencyLimiter(clientId, _ =>
                new ConcurrencyLimiterOptions
                {
                    PermitLimit = appSettings.RateLimit.MaxConcurrentPerClient,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 2
                });
        });

        // Custom rejection response
        options.OnRejected = async (context, cancellationToken) =>
        {
            context.HttpContext.Response.StatusCode = 429;
            context.HttpContext.Response.ContentType = "application/problem+json";

            var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
                ? retryAfterValue.TotalSeconds.ToString("0")
                : "60";

            context.HttpContext.Response.Headers["Retry-After"] = retryAfter;

            await context.HttpContext.Response.WriteAsJsonAsync(new
            {
                type = "https://httpstatuses.com/429",
                title = "Too many requests",
                status = 429,
                detail = $"Rate limit exceeded. Retry after {retryAfter} seconds.",
                retryAfterSeconds = retryAfter
            }, cancellationToken);
        };
    });

    Log.Information("Rate limiting enabled: {Window}s window, {ReqPerWindow} req/window, {Concurrent} concurrent",
        appSettings.RateLimit.WindowSeconds,
        appSettings.RateLimit.RequestsPerWindow,
        appSettings.RateLimit.MaxConcurrentPerClient);
}

// ── Core Services ──────────────────────────────────────────────────────────────

// Document Type Registry (plugin loader)
builder.Services.AddSingleton<DocumentTypeRegistry>();
builder.Services.AddSingleton<IDocumentTypeRegistry>(sp => sp.GetRequiredService<DocumentTypeRegistry>());

// PDF Processing
builder.Services.AddSingleton<PdfProcessorService>();

// Chunked / two-phase extraction (large contracts, well plans)
builder.Services.AddScoped<WellPlanTwoPhaseStrategy>();
builder.Services.AddScoped<ChunkedExtractionStrategy>();

// OpenAI Service (generic, prompt-agnostic)
builder.Services.AddSingleton<IGenericOpenAIService, GenericOpenAIService>();

// Validation Service (config-driven)
builder.Services.AddSingleton<ConfigurableValidationService>();

// Job Tracking (async batch progress)
builder.Services.AddSingleton<IJobTrackingService, DocumentExtractionService.Api.Services.InMemoryJobTrackingService>();

// Job Progress Reporter (centralizes stage transitions; extensibility point for SSE/SignalR)
builder.Services.AddSingleton<IJobProgressReporter, DocumentExtractionService.Api.Services.JobProgressReporter>();

// Extraction Result Store (for export endpoints)
builder.Services.AddSingleton<IExtractionResultStore, DocumentExtractionService.Api.Services.InMemoryExtractionResultStore>();

// Chat over extracted JSON (MVP)
builder.Services.AddSingleton<IExtractionChatService, ExtractionChatService>();

// Export Service (JSON + Excel generation)
builder.Services.AddSingleton<IExportService, DocumentExtractionService.Api.Services.ExportService>();

// Main Extraction Service (orchestrator)
builder.Services.AddScoped<IDocumentExtractionService, DocumentExtractionService.Core.Services.DocumentExtractionService>();

// ── Memory Cache ───────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache(opts =>
{
    opts.SizeLimit = appSettings.Cache.MaxSizeMb * 1024 * 1024;
});

// ── API Infrastructure ─────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.WriteIndented = true;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Generic Document Extraction Service",
        Version = "v1",
        Description = "AI-powered document data extraction. Supports Invoices, POs, Timesheets, and any custom document type via configuration.",
        Contact = new Microsoft.OpenApi.Models.OpenApiContact
        {
            Name = "Nabors AI Platform Team"
        }
    });

    // Add API Key auth to Swagger UI
    c.AddSecurityDefinition("ApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "API key for service authentication"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "ApiKey"
                }
            },
            Array.Empty<string>()
        }
    });
});

// CORS (configure for your frontend origins)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowConfigured", policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? [];

        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else if (!builder.Environment.IsProduction())
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
    });
});

// Health checks
builder.Services.AddHealthChecks();

// ══════════════════════════════════════════════════════════════════════════════
//  FIX v1.2: Configure request timeout for extraction endpoints.
//  Well plan extraction can take 3-10 minutes for large multi-well pads.
// ══════════════════════════════════════════════════════════════════════════════
builder.Services.AddRequestTimeouts(options =>
{
    // Default timeout for most endpoints
    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromMinutes(15)
    };

    // Named policy for extraction endpoints
    options.AddPolicy("Extraction", TimeSpan.FromMinutes(15));
});

// ── Build the App ──────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Initialize Document Type Registry ─────────────────────────────────────────
var registry = app.Services.GetRequiredService<DocumentTypeRegistry>();
await registry.InitializeAsync();

Log.Information("═══════════════════════════════════════════════════════════════");
Log.Information("  Generic Document Extraction Service v1.2");
Log.Information("  Document types loaded: {Count}", registry.GetAllDocumentTypes().Count);
Log.Information("  Types: {Types}",
    string.Join(", ", registry.GetAllDocumentTypes().Select(t => t.Id)));
Log.Information("  Azure OpenAI timeout: {Timeout} min",
    appSettings.AzureOpenAI.TimeoutMinutes > 0 ? appSettings.AzureOpenAI.TimeoutMinutes : 10);
Log.Information("═══════════════════════════════════════════════════════════════");

// ── Middleware Pipeline ────────────────────────────────────────────────────────

// Exception handling first (catches everything below)
app.UseMiddleware<ExceptionHandlingMiddleware>();

// Request logging
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Document Extraction Service v1");
        c.RoutePrefix = string.Empty; // Swagger UI at root
    });
}

app.UseHttpsRedirection();
app.UseCors("AllowConfigured");
app.UseAuthentication();
app.UseAuthorization();

// FIX v1.2: Enable request timeouts middleware
app.UseRequestTimeouts();

if (appSettings.RateLimit.Enabled)
{
    app.UseRateLimiter();
}

app.MapControllers();
app.MapHealthChecks("/health");

// ── Welcome message ────────────────────────────────────────────────────────────
app.MapGet("/", () => new
{
    service = "Generic Document Extraction Service",
    version = "1.2.0",
    status = "running",
    documentTypes = registry.GetAllDocumentTypes().Select(t => new { t.Id, t.DisplayName }),
    docs = "/swagger"
});

app.Run();
