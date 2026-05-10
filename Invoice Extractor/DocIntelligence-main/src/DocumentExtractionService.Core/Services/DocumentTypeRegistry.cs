using System.Collections.Concurrent;
using DocumentExtractionService.Core.Configuration;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Document Type Registry — the heart of the plugin system.
///
/// HOW IT WORKS:
/// 1. Scans DocumentTypes/ folder on startup
/// 2. For each subfolder, loads config.json + prompts + schema
/// 3. Validates the config and registers the document type
/// 4. If hot-reload is enabled, watches for changes and updates automatically
///
/// TO ADD A NEW DOCUMENT TYPE:
/// - Create DocumentTypes/{typeId}/config.json
/// - Create DocumentTypes/{typeId}/system_prompt.txt
/// - Create DocumentTypes/{typeId}/extraction_prompt.txt
/// - Create DocumentTypes/{typeId}/schema.json
/// - The type is available immediately (no restart if hot-reload = true)
///
/// FIX v1.2: Explicit JSON mapping from config.json camelCase keys to C# model.
/// The config.json files use camelCase (e.g., "extraction", "dualPass", "maxTokens")
/// but the C# model uses snake_case [JsonPropertyName] attributes. System.Text.Json's
/// PropertyNameCaseInsensitive does NOT bridge camelCase↔snake_case. This registry
/// now explicitly maps all extraction/dualPass/validation settings from config.json.
/// </summary>
public interface IDocumentTypeRegistry
{
    /// <summary>Get a document type config by ID. Returns null if not found.</summary>
    DocumentTypeConfig? GetDocumentType(string typeId);

    /// <summary>List all available (enabled) document types, including disabled ones.</summary>
    IReadOnlyList<DocumentTypeConfig> GetAllDocumentTypes(bool includeDisabled = false);

    /// <summary>Check if a document type exists and is enabled.</summary>
    bool IsValidDocumentType(string typeId);

    /// <summary>Force reload all document types from disk.</summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>Get the root folder path for document type configurations.</summary>
    string GetRootFolder();
}

public class DocumentTypeRegistry : IDocumentTypeRegistry, IDisposable
{
    private readonly DocumentTypeSettings _settings;
    private readonly ILogger<DocumentTypeRegistry> _logger;
    private readonly ConcurrentDictionary<string, DocumentTypeConfig> _registry = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private readonly string _rootFolder;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public DocumentTypeRegistry(
        IOptions<AppSettings> settings,
        ILogger<DocumentTypeRegistry> logger)
    {
        _settings = settings.Value.DocumentTypes;
        _logger = logger;
        _rootFolder = Path.IsPathRooted(_settings.RootFolder)
            ? _settings.RootFolder
            : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.RootFolder);
    }

    /// <summary>Load all document types at startup.</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await ReloadAsync(cancellationToken);

        if (_settings.EnableHotReload)
        {
            SetupFileWatcher();
        }
    }

    public DocumentTypeConfig? GetDocumentType(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return null;
        return _registry.TryGetValue(typeId.ToLowerInvariant(), out var config)
            ? config : null;
    }

    public IReadOnlyList<DocumentTypeConfig> GetAllDocumentTypes(bool includeDisabled = false)
        => includeDisabled
            ? _registry.Values.OrderBy(c => c.DisplayName).ToList()
            : _registry.Values.Where(c => c.Enabled).OrderBy(c => c.DisplayName).ToList();

    public string GetRootFolder() => _rootFolder;

    public bool IsValidDocumentType(string typeId)
    {
        if (string.IsNullOrWhiteSpace(typeId)) return false;
        var config = GetDocumentType(typeId);
        return config != null && config.Enabled;
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_rootFolder))
        {
            _logger.LogWarning("DocumentTypes folder not found: {Folder}. Creating...", _rootFolder);
            Directory.CreateDirectory(_rootFolder);
        }

        var typeDirs = Directory.GetDirectories(_rootFolder);
        var loaded = 0;

        foreach (var dir in typeDirs)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var typeId = Path.GetFileName(dir).ToLowerInvariant();
            try
            {
                var config = await LoadDocumentTypeAsync(dir, typeId);
                if (config != null)
                {
                    _registry[typeId] = config;
                    loaded++;
                    _logger.LogInformation("✓ Loaded document type: {TypeId} ({DisplayName}) — " +
                        "maxPages={MaxPages}, maxTokens={MaxTokens}, dualPass={DualPass}",
                        typeId, config.DisplayName,
                        config.ExtractionSettings.MaxPagesForVision,
                        config.ExtractionSettings.MaxTokens,
                        config.DualPass.Enabled);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "✗ Failed to load document type from: {Dir}", dir);
            }
        }

        _logger.LogInformation("Document type registry initialized: {Count}/{Total} types loaded",
            loaded, typeDirs.Length);
    }

    private async Task<DocumentTypeConfig?> LoadDocumentTypeAsync(string folder, string typeId)
    {
        var configFile = Path.Combine(folder, "config.json");

        if (!File.Exists(configFile))
        {
            _logger.LogWarning("Skipping {TypeId}: config.json not found in {Folder}", typeId, folder);
            return null;
        }

        // ── Load config ──
        var configJson = await File.ReadAllTextAsync(configFile);

        // First: basic deserialization for simple top-level fields
        var config = JsonSerializer.Deserialize<DocumentTypeConfig>(configJson, JsonOptions);

        if (config == null)
        {
            _logger.LogWarning("Could not parse config.json for {TypeId}", typeId);
            return null;
        }

        config.Id = typeId;
        config.FolderPath = folder;

        // ══════════════════════════════════════════════════════════════════════
        //  FIX v1.2: EXPLICIT MAPPING FROM CONFIG.JSON CAMELCASE KEYS
        //
        //  config.json uses: "extraction", "dualPass", "displayName", etc.
        //  C# model has [JsonPropertyName("extraction_settings")], etc.
        //  PropertyNameCaseInsensitive does NOT bridge camelCase ↔ snake_case.
        //  So we manually read these sections from the raw JSON.
        // ══════════════════════════════════════════════════════════════════════
        using var configDoc = JsonDocument.Parse(configJson);
        var root = configDoc.RootElement;

        // Map top-level fields that may not have deserialized correctly
        if (config.DisplayName == "" && root.TryGetProperty("displayName", out var dn))
            config.DisplayName = dn.GetString() ?? "";

        if (config.MaxFileSizeMb == 0 && root.TryGetProperty("maxFileSizeMb", out var mfs))
            config.MaxFileSizeMb = mfs.GetInt32();

        if (config.MaxPages == 0 && root.TryGetProperty("maxPages", out var mp))
            config.MaxPages = mp.GetInt32();

        if (config.IconName == null && root.TryGetProperty("iconName", out var ic))
            config.IconName = ic.GetString();

        if (config.Category == null && root.TryGetProperty("category", out var cat))
            config.Category = cat.GetString();

        if (config.AcceptedExtensions.Count == 0 && root.TryGetProperty("acceptedFileTypes", out var aft))
        {
            config.AcceptedExtensions = aft.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        // ── Map extraction settings (config.json key: "extraction") ──
        MapExtractionSettings(root, config);

        // ── Map dual pass config (config.json key: "dualPass") ──
        MapDualPassConfig(root, config);

        // ── Map output config (config.json key: "output") ──
        MapOutputConfig(root, config);

        if (!config.Enabled)
        {
            _logger.LogInformation("  Skipping disabled document type: {TypeId}", typeId);
            return config;
        }

        // ── Load system prompt ──
        var systemPromptFile = Path.Combine(folder, config.SystemPromptFile);
        if (!File.Exists(systemPromptFile))
        {
            _logger.LogError("System prompt file not found for {TypeId}: {File}", typeId, systemPromptFile);
            return null;
        }
        config.SystemPrompt = await File.ReadAllTextAsync(systemPromptFile);

        // ── Load extraction prompt ──
        var extractionPromptFile = Path.Combine(folder, config.ExtractionPromptFile);
        if (!File.Exists(extractionPromptFile))
        {
            _logger.LogError("Extraction prompt file not found for {TypeId}: {File}", typeId, extractionPromptFile);
            return null;
        }
        config.ExtractionPromptTemplate = await File.ReadAllTextAsync(extractionPromptFile);

        if (config.Chunking?.Enabled == true)
        {
            var chunkPromptFileName = config.Chunking.ChunkExtractionPromptFile
                ?? "chunk_extraction_prompt.txt";
            var chunkPromptPath = Path.Combine(folder, chunkPromptFileName);
            if (File.Exists(chunkPromptPath))
            {
                config.ChunkExtractionPrompt = await File.ReadAllTextAsync(chunkPromptPath);
                _logger.LogDebug("  Loaded chunk extraction prompt: {File} ({Len} chars)",
                    chunkPromptFileName, config.ChunkExtractionPrompt.Length);
            }
            else
            {
                _logger.LogDebug(
                    "  No chunk extraction prompt at {Path} — chunk calls will use the main extraction prompt",
                    chunkPromptPath);
            }

            if (string.Equals(config.Chunking.MergeStrategy, "two_phase", StringComparison.OrdinalIgnoreCase))
            {
                config.MapPrompt = await LoadOptionalTextFileAsync(
                    folder, config.Chunking.MapPromptFile, "map_prompt.txt");
                config.MapSchema = await LoadOptionalTextFileAsync(
                    folder, config.Chunking.MapSchemaFile, "map_schema.json");
                config.PerWellPrompt = await LoadOptionalTextFileAsync(
                    folder, config.Chunking.PerItemPromptFile, "per_well_prompt.txt");
                config.PerWellSchema = await LoadOptionalTextFileAsync(
                    folder, config.Chunking.PerItemSchemaFile, "per_well_schema.json");
            }
        }

        // ── Load schema ──
        var schemaFile = Path.Combine(folder, config.SchemaFile);
        if (!File.Exists(schemaFile))
        {
            _logger.LogError("Schema file not found for {TypeId}: {File}", typeId, schemaFile);
            return null;
        }
        config.JsonSchema = await File.ReadAllTextAsync(schemaFile);

        // Validate schema is valid JSON
        try
        {
            using var _ = JsonDocument.Parse(config.JsonSchema);
        }
        catch (JsonException ex)
        {
            _logger.LogError("Invalid JSON schema for {TypeId}: {Error}", typeId, ex.Message);
            return null;
        }

        // ── Load validation rules ──
        // Support BOTH external file AND inline rules in config.json
        if (!string.IsNullOrEmpty(config.ValidationRulesFile))
        {
            var rulesFile = Path.Combine(folder, config.ValidationRulesFile);
            if (File.Exists(rulesFile))
            {
                var rulesJson = await File.ReadAllTextAsync(rulesFile);
                config.ValidationRules = JsonSerializer.Deserialize<List<ValidationRule>>(rulesJson, JsonOptions)
                    ?? [];
                _logger.LogDebug("  Loaded {Count} validation rules from file for {TypeId}",
                    config.ValidationRules.Count, typeId);
            }
        }
        else
        {
            // Try inline validation rules from config.json
            MapInlineValidationRules(root, config);
        }

        return config;
    }

    /// <summary>
    /// Explicitly map "extraction" block from config.json to ExtractionSettings.
    /// Handles both "extraction" (config.json format) and "extraction_settings" (C# format).
    /// </summary>
    private void MapExtractionSettings(JsonElement root, DocumentTypeConfig config)
    {
        JsonElement extraction;
        if (!root.TryGetProperty("extraction", out extraction) &&
            !root.TryGetProperty("extractionSettings", out extraction) &&
            !root.TryGetProperty("extraction_settings", out extraction))
        {
            _logger.LogDebug("  No extraction settings found in config.json for {TypeId}, using defaults", config.Id);
            return;
        }

        var settings = config.ExtractionSettings;

        if (extraction.TryGetProperty("maxPagesForVision", out var mpv) ||
            extraction.TryGetProperty("max_pages_for_vision", out mpv))
            settings.MaxPagesForVision = mpv.GetInt32();

        // config.json uses "imageResolutionDpi" but C# property is "ImageDpi"
        if (extraction.TryGetProperty("imageResolutionDpi", out var dpi) ||
            extraction.TryGetProperty("imageDpi", out dpi) ||
            extraction.TryGetProperty("image_dpi", out dpi))
            settings.ImageDpi = dpi.GetInt32();

        if (extraction.TryGetProperty("imageMaxWidthPx", out var mw) ||
            extraction.TryGetProperty("image_max_width_px", out mw))
            settings.ImageMaxWidthPx = mw.GetInt32();

        if (extraction.TryGetProperty("reasoningEffort", out var re) ||
            extraction.TryGetProperty("reasoning_effort", out re))
            settings.ReasoningEffort = re.GetString() ?? "medium";

        if (extraction.TryGetProperty("maxTokens", out var mt) ||
            extraction.TryGetProperty("max_tokens", out mt))
            settings.MaxTokens = mt.GetInt32();

        if (extraction.TryGetProperty("temperature", out var temp))
            settings.Temperature = temp.GetSingle();

        if (extraction.TryGetProperty("maxTextChars", out var mtc) ||
            extraction.TryGetProperty("max_text_chars", out mtc))
            settings.MaxTextChars = mtc.GetInt32();

        if (extraction.TryGetProperty("textTruncation", out var ttrunc) ||
            extraction.TryGetProperty("text_truncation", out ttrunc))
            settings.TextTruncation = ttrunc.GetString() ?? "head_only";

        _logger.LogDebug("  Extraction settings mapped: maxPages={MaxPages}, maxTokens={MaxTokens}, " +
            "maxTextChars={MaxText}, textTruncation={Tt}, dpi={Dpi}, effort={Effort}",
            settings.MaxPagesForVision, settings.MaxTokens, settings.MaxTextChars, settings.TextTruncation,
            settings.ImageDpi, settings.ReasoningEffort);
    }

    /// <summary>
    /// Explicitly map "dualPass" block from config.json to DualPassConfig.
    /// </summary>
    private void MapDualPassConfig(JsonElement root, DocumentTypeConfig config)
    {
        JsonElement dp;
        if (!root.TryGetProperty("dualPass", out dp) &&
            !root.TryGetProperty("dual_pass", out dp))
            return;

        if (dp.TryGetProperty("enabled", out var en))
            config.DualPass.Enabled = en.GetBoolean();

        if (dp.TryGetProperty("criticalFields", out var cf) ||
            dp.TryGetProperty("critical_fields", out cf))
        {
            config.DualPass.CriticalFields = cf.EnumerateArray()
                .Select(e => e.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();
        }

        if (dp.TryGetProperty("confidenceThreshold", out var ct) ||
            dp.TryGetProperty("confidence_threshold", out ct))
            config.DualPass.ConfidenceThreshold = ct.GetDecimal();

        if (dp.TryGetProperty("confidencePath", out var cp) ||
            dp.TryGetProperty("confidence_path", out cp))
            config.DualPass.ConfidencePath = cp.GetString() ?? "confidence";
    }

    /// <summary>
    /// Map "output" block from config.json to DocumentOutputConfig.
    /// </summary>
    private static void MapOutputConfig(JsonElement root, DocumentTypeConfig config)
    {
        if (!root.TryGetProperty("output", out var output))
            return;

        if (output.TryGetProperty("includeMetadata", out var im) ||
            output.TryGetProperty("include_metadata", out im))
            config.Output.IncludeMetadata = im.GetBoolean();

        if (output.TryGetProperty("includeRawText", out var irt) ||
            output.TryGetProperty("include_raw_text", out irt))
            config.Output.IncludeRawText = irt.GetBoolean();

        if (output.TryGetProperty("excelExportEnabled", out var ee) ||
            output.TryGetProperty("excel_export_enabled", out ee))
            config.Output.ExcelExportEnabled = ee.GetBoolean();
    }

    /// <summary>
    /// Map inline "validation.rules" from config.json to ValidationRules list.
    /// This allows document type authors to embed validation rules directly in config.json.
    /// </summary>
    private void MapInlineValidationRules(JsonElement root, DocumentTypeConfig config)
    {
        if (!root.TryGetProperty("validation", out var validation))
            return;

        if (!validation.TryGetProperty("rules", out var rulesArray))
            return;

        var rules = new List<ValidationRule>();
        foreach (var ruleEl in rulesArray.EnumerateArray())
        {
            var rule = new ValidationRule();

            if (ruleEl.TryGetProperty("field", out var field))
                rule.Field = field.GetString() ?? "";

            if (ruleEl.TryGetProperty("type", out var type))
                rule.Type = type.GetString() ?? "required";

            if (ruleEl.TryGetProperty("severity", out var sev))
            {
                var sevStr = sev.GetString()?.ToLowerInvariant() ?? "warning";
                rule.Severity = sevStr switch
                {
                    "error" => ValidationSeverity.Error,
                    "info" => ValidationSeverity.Info,
                    _ => ValidationSeverity.Warning
                };
            }

            if (ruleEl.TryGetProperty("message", out var msg))
                rule.Message = msg.GetString() ?? "";

            rules.Add(rule);
        }

        config.ValidationRules = rules;
        _logger.LogDebug("  Loaded {Count} inline validation rules for {TypeId}",
            rules.Count, config.Id);
    }

    private async Task<string?> LoadOptionalTextFileAsync(string typeFolder, string? fileName, string defaultName)
    {
        var fn = fileName ?? defaultName;
        var path = Path.Combine(typeFolder, fn);
        if (!File.Exists(path)) return null;
        var content = await File.ReadAllTextAsync(path);
        _logger.LogDebug("  Loaded two-phase / optional file: {File} ({Len} chars)", fn, content.Length);
        return content;
    }

    private void SetupFileWatcher()
    {
        if (!Directory.Exists(_rootFolder)) return;

        _watcher = new FileSystemWatcher(_rootFolder)
        {
            Filter = "*.json",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
        };

        // Debounce rapid file changes
        var debounceTimer = new Dictionary<string, System.Timers.Timer>();

        _watcher.Changed += async (_, e) => await HandleFileChangeAsync(e.FullPath);
        _watcher.Created += async (_, e) => await HandleFileChangeAsync(e.FullPath);
        _watcher.Deleted += async (_, e) => await HandleFileChangeAsync(e.FullPath);
        _watcher.Renamed += async (_, e) => await HandleFileChangeAsync(e.FullPath);

        _watcher.EnableRaisingEvents = true;
        _logger.LogInformation("Hot-reload enabled for DocumentTypes folder: {Folder}", _rootFolder);
    }

    private async Task HandleFileChangeAsync(string changedFile)
    {
        // Get the type ID from the changed file path
        var relativePath = Path.GetRelativePath(_rootFolder, changedFile);
        var parts = relativePath.Split(Path.DirectorySeparatorChar);
        if (parts.Length < 2) return;

        var typeId = parts[0].ToLowerInvariant();
        var typeFolder = Path.Combine(_rootFolder, typeId);

        await Task.Delay(500); // Brief debounce

        try
        {
            _logger.LogInformation("Hot-reload: reloading document type '{TypeId}' due to file change: {File}",
                typeId, Path.GetFileName(changedFile));

            var config = await LoadDocumentTypeAsync(typeFolder, typeId);
            if (config != null)
            {
                _registry[typeId] = config;
                _logger.LogInformation("✓ Hot-reloaded document type: {TypeId}", typeId);
            }
            else
            {
                // If load failed (e.g., file deleted), optionally remove from registry
                _logger.LogWarning("Hot-reload failed for {TypeId}, keeping previous version", typeId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hot-reload error for {TypeId}", typeId);
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
