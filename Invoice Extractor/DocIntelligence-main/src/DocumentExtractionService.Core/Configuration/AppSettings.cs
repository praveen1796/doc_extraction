namespace DocumentExtractionService.Core.Configuration;

/// <summary>
/// Root configuration for the Document Extraction Service.
/// Bound from appsettings.json and environment variables.
/// </summary>
public class AppSettings
{
    public AzureOpenAISettings AzureOpenAI { get; set; } = new();
    public ProcessingSettings Processing { get; set; } = new();
    public AuthSettings Auth { get; set; } = new();
    public RateLimitSettings RateLimit { get; set; } = new();
    public DocumentTypeSettings DocumentTypes { get; set; } = new();
    public StorageSettings Storage { get; set; } = new();
    public CacheSettings Cache { get; set; } = new();
}

public class AzureOpenAISettings
{
    public string Endpoint { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string DeploymentName { get; set; } = "gpt-4o";
    public string ApiVersion { get; set; } = "2025-04-01-preview";
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 2000;

    /// <summary>
    /// HTTP timeout in minutes for Azure OpenAI API calls.
    /// Default: 10 minutes. Well plan extraction with multiple images
    /// can take 3-8 minutes for GPT to process.
    /// 
    /// FIX v1.2: Added to prevent default 100-second HttpClient timeout
    /// from killing long-running extractions.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 10;
}

public class ProcessingSettings
{
    public int MaxFileSizeMb { get; set; } = 50;
    public int MaxBatchSize { get; set; } = 50;
    public int DefaultParallelism { get; set; } = 2;
    public int AsyncJobTimeoutMinutes { get; set; } = 30;
    public bool EnableDualPassByDefault { get; set; } = true;
}

public class AuthSettings
{
    /// <summary>Enable/disable authentication (disable for local dev).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Azure AD JWT Bearer settings.</summary>
    public AzureAdSettings AzureAd { get; set; } = new();

    /// <summary>API Key authentication settings (fallback / service-to-service).</summary>
    public ApiKeySettings ApiKey { get; set; } = new();
}

public class AzureAdSettings
{
    public bool Enabled { get; set; } = true;
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string Audience { get; set; } = "";

    /// <summary>Required roles for extraction endpoint (empty = any authenticated user).</summary>
    public List<string> RequiredRoles { get; set; } = [];

    /// <summary>Admin roles for document type management endpoints.</summary>
    public List<string> AdminRoles { get; set; } = ["DocExtract.Admin"];
}

public class ApiKeySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Header name for API key. Default: X-Api-Key.
    /// Set to empty to use Authorization: Bearer {apiKey}.
    /// </summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// API key store: "config" (from appsettings) or "azure_keyvault" or "database".
    /// </summary>
    public string Store { get; set; } = "config";

    /// <summary>
    /// API keys when Store = "config".
    /// Key = API key value, Value = client name/identifier.
    /// In production, use environment variables: Auth__ApiKey__Keys__0=key:client
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = new();
}

public class RateLimitSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Sliding window duration in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Max requests per window per client.</summary>
    public int RequestsPerWindow { get; set; } = 60;

    /// <summary>Max requests per day per client.</summary>
    public int RequestsPerDay { get; set; } = 1000;

    /// <summary>Max concurrent requests per client.</summary>
    public int MaxConcurrentPerClient { get; set; } = 5;

    /// <summary>Queue pending requests instead of rejecting (429).</summary>
    public bool QueueRequests { get; set; } = false;

    /// <summary>Queue depth before rejecting (if QueueRequests = true).</summary>
    public int MaxQueueDepth { get; set; } = 10;
}

public class DocumentTypeSettings
{
    /// <summary>
    /// Root folder containing document type subdirectories.
    /// Default: "DocumentTypes" (relative to app base directory).
    /// Each subdirectory = one document type with config.json + prompts + schema.
    /// </summary>
    public string RootFolder { get; set; } = "DocumentTypes";

    /// <summary>
    /// Watch for changes and hot-reload document types without restart.
    /// </summary>
    public bool EnableHotReload { get; set; } = true;

    /// <summary>Cache loaded configs for this many minutes.</summary>
    public int CacheMinutes { get; set; } = 60;
}

public class StorageSettings
{
    /// <summary>
    /// Where to temporarily store uploaded files during processing.
    /// "memory" = in-memory (default, fast, limited by RAM).
    /// "disk" = temp folder on disk (handles large files).
    /// "azure_blob" = Azure Blob Storage (for distributed deployments).
    /// </summary>
    public string Provider { get; set; } = "memory";

    public string TempFolder { get; set; } = "temp";
    public string AzureBlobConnectionString { get; set; } = "";
    public string AzureBlobContainer { get; set; } = "doc-extraction-temp";
}

public class CacheSettings
{
    public bool Enabled { get; set; } = true;
    public int DefaultExpiryMinutes { get; set; } = 30;
    public int MaxSizeMb { get; set; } = 500;
}
