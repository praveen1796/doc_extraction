using System.Text.Json;
using System.Text.Json.Serialization;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocumentExtractionService.Api.Controllers;

[ApiController]
[Route("api/v1/document-types")]
[Authorize]
public class DocumentTypesController : ControllerBase
{
    private readonly IDocumentTypeRegistry _registry;
    private readonly IDocumentExtractionService _extractionService;
    private readonly ILogger<DocumentTypesController> _logger;

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public DocumentTypesController(
        IDocumentTypeRegistry registry,
        IDocumentExtractionService extractionService,
        ILogger<DocumentTypesController> logger)
    {
        _registry = registry;
        _extractionService = extractionService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(DocumentTypeSummary[]), StatusCodes.Status200OK)]
    public ActionResult<DocumentTypeSummary[]> GetAll()
    {
        var types = _registry.GetAllDocumentTypes().Select(MapToSummary).ToArray();
        return Ok(types);
    }

    [HttpGet("{typeId}")]
    [ProducesResponseType(typeof(DocumentTypeDetail), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DocumentTypeDetail> GetById(string typeId)
    {
        var config = _registry.GetDocumentType(typeId);
        if (config == null)
            return NotFound(Problem(title: "Document type not found", detail: $"No document type with ID '{typeId}' is registered.", statusCode: 404));
        return Ok(MapToDetail(config));
    }

    [HttpGet("{typeId}/prompts")]
    [ProducesResponseType(typeof(DocumentTypePrompts), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<DocumentTypePrompts> GetPrompts(string typeId)
    {
        var config = _registry.GetDocumentType(typeId);
        if (config == null)
            return NotFound(Problem(title: "Not found", detail: $"Document type '{typeId}' not found.", statusCode: 404));
        return Ok(new DocumentTypePrompts
        {
            TypeId = config.Id,
            SystemPrompt = config.SystemPrompt,
            ExtractionPrompt = config.ExtractionPromptTemplate,
            JsonSchema = config.JsonSchema,
            ValidationRules = config.ValidationRules
        });
    }

    [HttpPost("reload")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(ReloadResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<ReloadResult>> Reload(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Manual reload requested by {User}", User.Identity?.Name);
        var before = _registry.GetAllDocumentTypes().Count;
        await _registry.ReloadAsync(cancellationToken);
        var after = _registry.GetAllDocumentTypes().Count;
        return Ok(new ReloadResult { TypesBefore = before, TypesAfter = after, Types = _registry.GetAllDocumentTypes().Select(t => t.Id).ToList() });
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(DocumentTypeSummary), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DocumentTypeSummary>> Create([FromBody] CreateDocumentTypeRequest request, CancellationToken cancellationToken)
    {
        var typeId = SanitizeTypeId(request.TypeId);
        if (string.IsNullOrWhiteSpace(typeId))
            return BadRequest(Problem(title: "Invalid type ID", detail: "Type ID must be alphanumeric with underscores.", statusCode: 400));
        if (_registry.IsValidDocumentType(typeId) || _registry.GetDocumentType(typeId) != null)
            return Conflict(Problem(title: "Already exists", detail: $"Document type '{typeId}' already exists.", statusCode: 409));
        if (!string.IsNullOrWhiteSpace(request.JsonSchema))
        {
            try { using var _ = JsonDocument.Parse(request.JsonSchema); }
            catch (JsonException ex) { return BadRequest(Problem(title: "Invalid JSON schema", detail: ex.Message, statusCode: 400)); }
        }

        var rootFolder = _registry.GetRootFolder();
        var typeFolder = Path.Combine(rootFolder, typeId);
        Directory.CreateDirectory(typeFolder);
        try
        {
            var config = BuildConfigJson(request, typeId);
            await System.IO.File.WriteAllTextAsync(Path.Combine(typeFolder, "config.json"), JsonSerializer.Serialize(config, JsonWriteOptions), cancellationToken);
            await System.IO.File.WriteAllTextAsync(Path.Combine(typeFolder, "system_prompt.txt"), request.SystemPrompt ?? GetDefaultSystemPrompt(request.DisplayName), cancellationToken);
            await System.IO.File.WriteAllTextAsync(Path.Combine(typeFolder, "extraction_prompt.txt"), request.ExtractionPrompt ?? GetDefaultExtractionPrompt(request.DisplayName), cancellationToken);
            await System.IO.File.WriteAllTextAsync(Path.Combine(typeFolder, "schema.json"), request.JsonSchema ?? GetDefaultSchema(), cancellationToken);
            await _registry.ReloadAsync(cancellationToken);
            var created = _registry.GetDocumentType(typeId);
            if (created == null) return StatusCode(500, Problem(title: "Creation failed", detail: "Files written but registry failed to load.", statusCode: 500));
            _logger.LogInformation("Created document type '{TypeId}' by {User}", typeId, User.Identity?.Name);
            return CreatedAtAction(nameof(GetById), new { typeId }, MapToSummary(created));
        }
        catch (Exception ex)
        {
            if (Directory.Exists(typeFolder)) Directory.Delete(typeFolder, recursive: true);
            _logger.LogError(ex, "Failed to create document type '{TypeId}'", typeId);
            return StatusCode(500, Problem(title: "Creation failed", detail: ex.Message, statusCode: 500));
        }
    }

    [HttpPut("{typeId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(typeof(DocumentTypeSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DocumentTypeSummary>> Update(string typeId, [FromBody] UpdateDocumentTypeRequest request, CancellationToken cancellationToken)
    {
        var existing = _registry.GetDocumentType(typeId);
        if (existing == null) return NotFound(Problem(title: "Not found", detail: $"Document type '{typeId}' not found.", statusCode: 404));
        var folder = existing.FolderPath;
        try
        {
            if (HasConfigChanges(request))
            {
                var configPath = Path.Combine(folder, "config.json");
                var currentJson = await System.IO.File.ReadAllTextAsync(configPath, cancellationToken);
                var configDoc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(currentJson) ?? new();
                if (request.DisplayName != null) configDoc["display_name"] = JsonSerializer.SerializeToElement(request.DisplayName);
                if (request.Description != null) configDoc["description"] = JsonSerializer.SerializeToElement(request.Description);
                if (request.Category != null) configDoc["category"] = JsonSerializer.SerializeToElement(request.Category);
                if (request.IconName != null) configDoc["icon_name"] = JsonSerializer.SerializeToElement(request.IconName);
                if (request.Enabled.HasValue) configDoc["enabled"] = JsonSerializer.SerializeToElement(request.Enabled.Value);
                if (request.AcceptedExtensions != null) configDoc["accepted_extensions"] = JsonSerializer.SerializeToElement(request.AcceptedExtensions);
                if (request.MaxFileSizeMb.HasValue) configDoc["max_file_size_mb"] = JsonSerializer.SerializeToElement(request.MaxFileSizeMb.Value);
                if (request.MaxPages.HasValue) configDoc["max_pages"] = JsonSerializer.SerializeToElement(request.MaxPages.Value);
                if (request.Version != null) configDoc["version"] = JsonSerializer.SerializeToElement(request.Version);
                await System.IO.File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(configDoc, JsonWriteOptions), cancellationToken);
            }
            if (request.SystemPrompt != null) await System.IO.File.WriteAllTextAsync(Path.Combine(folder, existing.SystemPromptFile), request.SystemPrompt, cancellationToken);
            if (request.ExtractionPrompt != null) await System.IO.File.WriteAllTextAsync(Path.Combine(folder, existing.ExtractionPromptFile), request.ExtractionPrompt, cancellationToken);
            if (request.JsonSchema != null)
            {
                try { using var _ = JsonDocument.Parse(request.JsonSchema); }
                catch (JsonException ex) { return BadRequest(Problem(title: "Invalid JSON schema", detail: ex.Message, statusCode: 400)); }
                await System.IO.File.WriteAllTextAsync(Path.Combine(folder, existing.SchemaFile), request.JsonSchema, cancellationToken);
            }
            await _registry.ReloadAsync(cancellationToken);
            var updated = _registry.GetDocumentType(typeId);
            _logger.LogInformation("Updated document type '{TypeId}' by {User}", typeId, User.Identity?.Name);
            return Ok(MapToSummary(updated!));
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to update '{TypeId}'", typeId); return StatusCode(500, Problem(title: "Update failed", detail: ex.Message, statusCode: 500)); }
    }

    [HttpDelete("{typeId}")]
    [Authorize(Policy = "AdminOnly")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string typeId, CancellationToken cancellationToken)
    {
        var existing = _registry.GetDocumentType(typeId);
        if (existing == null) return NotFound(Problem(title: "Not found", detail: $"Document type '{typeId}' not found.", statusCode: 404));
        try
        {
            var archiveRoot = Path.Combine(Path.GetDirectoryName(existing.FolderPath)!, "_deleted");
            Directory.CreateDirectory(archiveRoot);
            Directory.Move(existing.FolderPath, Path.Combine(archiveRoot, $"{typeId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}"));
            await _registry.ReloadAsync(cancellationToken);
            _logger.LogInformation("Deleted (archived) document type '{TypeId}' by {User}", typeId, User.Identity?.Name);
            return NoContent();
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to delete '{TypeId}'", typeId); return StatusCode(500, Problem(title: "Delete failed", detail: ex.Message, statusCode: 500)); }
    }

    [HttpPost("{typeId}/test-extract")]
    [Authorize(Policy = "AdminOnly")]
    [RequestSizeLimit(52_428_800)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ExtractionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExtractionResponse>> TestExtract(string typeId, [FromForm] IFormFile file, CancellationToken cancellationToken)
    {
        if (!_registry.IsValidDocumentType(typeId)) return NotFound(Problem(title: "Not found", detail: $"Document type '{typeId}' not found.", statusCode: 404));
        if (file == null || file.Length == 0) return BadRequest(Problem(title: "No file", detail: "Upload a sample document.", statusCode: 400));
        await using var stream = file.OpenReadStream();
        var result = await _extractionService.ExtractAsync(stream, file.FileName, typeId, requestId: $"test_{Guid.NewGuid():N}", cancellationToken: cancellationToken);
        return Ok(result);
    }

    // ── Helpers ──
    private static string SanitizeTypeId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        return new string(input.ToLowerInvariant().Replace(' ', '_').Replace('-', '_').Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray()).Trim('_');
    }
    private static bool HasConfigChanges(UpdateDocumentTypeRequest r) => r.DisplayName != null || r.Description != null || r.Category != null || r.IconName != null || r.Enabled.HasValue || r.AcceptedExtensions != null || r.MaxFileSizeMb.HasValue || r.MaxPages.HasValue || r.Version != null;
    private static object BuildConfigJson(CreateDocumentTypeRequest r, string typeId) => new { id = typeId, display_name = r.DisplayName ?? typeId, description = r.Description ?? "", version = r.Version ?? "1.0.0", enabled = r.Enabled ?? true, accepted_extensions = r.AcceptedExtensions ?? new List<string> { ".pdf" }, max_file_size_mb = r.MaxFileSizeMb ?? 50, max_pages = r.MaxPages ?? 30, icon_name = r.IconName ?? "file-text", category = r.Category ?? "General", system_prompt_file = "system_prompt.txt", extraction_prompt_file = "extraction_prompt.txt", schema_file = "schema.json", extraction_settings = new { max_pages_for_vision = r.MaxPagesForVision ?? 12, image_dpi = 200, image_max_width_px = 2048, reasoning_effort = r.ReasoningEffort ?? "medium", max_tokens = r.MaxTokens ?? 8192, temperature = 0.0 }, dual_pass = new { enabled = r.DualPassEnabled ?? true, critical_fields = r.DualPassCriticalFields ?? new List<string>(), confidence_threshold = 0.70, confidence_path = "confidence" }, output = new { include_metadata = true, include_raw_text = false, indent_json = true, excel_export_enabled = r.ExcelExportEnabled ?? true } };
    private static string GetDefaultSystemPrompt(string? n) => $"You are an expert document data extraction specialist.\n\nYour task: Extract structured data from {n ?? "documents"} with high accuracy.\nReturn ONLY valid JSON matching the provided schema.\n\nRULES:\n1. Extract all fields specified in the schema\n2. Use null for fields that cannot be found\n3. Dates must be YYYY-MM-DD format\n4. For monetary amounts, extract numeric values only\n5. Include confidence scores (0.0-1.0) for key fields";
    private static string GetDefaultExtractionPrompt(string? n) => $"Extract all fields from this {n ?? "document"} according to the JSON schema provided.\n\nExamine every page carefully.\nReturn a single JSON object with all extracted fields.\nFor any field you cannot find, set the value to null.\nInclude a \"confidence\" object with scores (0.0-1.0) for main fields.";
    private static string GetDefaultSchema() => "{\n  \"type\": \"object\",\n  \"properties\": {\n    \"document_title\": { \"type\": [\"string\", \"null\"] },\n    \"document_date\": { \"type\": [\"string\", \"null\"] },\n    \"extracted_fields\": { \"type\": \"object\", \"additionalProperties\": true },\n    \"confidence\": { \"type\": \"object\", \"additionalProperties\": { \"type\": \"number\" } }\n  },\n  \"required\": [\"document_title\", \"extracted_fields\"],\n  \"additionalProperties\": false\n}";
    private static DocumentTypeSummary MapToSummary(DocumentTypeConfig t) => new() { Key = t.Id, DisplayName = t.DisplayName, Description = t.Description, Version = t.Version, Enabled = t.Enabled, AcceptedFileTypes = t.AcceptedExtensions, MaxFileSizeMb = t.MaxFileSizeMb, MaxPageCount = t.MaxPages, IconName = t.IconName, Category = t.Category, SupportsBatch = true, SupportsExcelExport = t.Output.ExcelExportEnabled, SupportsJsonExport = true, DualPassEnabled = t.DualPass.Enabled, SampleFields = t.SampleFields };
    private static DocumentTypeDetail MapToDetail(DocumentTypeConfig c) => new() { Key = c.Id, DisplayName = c.DisplayName, Description = c.Description, Version = c.Version, Enabled = c.Enabled, AcceptedFileTypes = c.AcceptedExtensions, MaxFileSizeMb = c.MaxFileSizeMb, MaxPageCount = c.MaxPages, IconName = c.IconName, Category = c.Category, SupportsBatch = true, SupportsExcelExport = c.Output.ExcelExportEnabled, SupportsJsonExport = true, ExtractionSettings = c.ExtractionSettings, DualPass = c.DualPass, Output = c.Output, ValidationRuleCount = c.ValidationRules.Count, SampleFields = c.SampleFields };
}

// ── DTOs ──
public class CreateDocumentTypeRequest
{
    [JsonPropertyName("type_id")] public string TypeId { get; set; } = "";
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("icon_name")] public string? IconName { get; set; }
    [JsonPropertyName("accepted_extensions")] public List<string>? AcceptedExtensions { get; set; }
    [JsonPropertyName("max_file_size_mb")] public int? MaxFileSizeMb { get; set; }
    [JsonPropertyName("max_pages")] public int? MaxPages { get; set; }
    [JsonPropertyName("system_prompt")] public string? SystemPrompt { get; set; }
    [JsonPropertyName("extraction_prompt")] public string? ExtractionPrompt { get; set; }
    [JsonPropertyName("json_schema")] public string? JsonSchema { get; set; }
    [JsonPropertyName("validation_rules")] public List<ValidationRule>? ValidationRules { get; set; }
    [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; set; }
    [JsonPropertyName("max_tokens")] public int? MaxTokens { get; set; }
    [JsonPropertyName("max_pages_for_vision")] public int? MaxPagesForVision { get; set; }
    [JsonPropertyName("dual_pass_enabled")] public bool? DualPassEnabled { get; set; }
    [JsonPropertyName("dual_pass_critical_fields")] public List<string>? DualPassCriticalFields { get; set; }
    [JsonPropertyName("excel_export_enabled")] public bool? ExcelExportEnabled { get; set; }
}
public class UpdateDocumentTypeRequest
{
    [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("icon_name")] public string? IconName { get; set; }
    [JsonPropertyName("accepted_extensions")] public List<string>? AcceptedExtensions { get; set; }
    [JsonPropertyName("max_file_size_mb")] public int? MaxFileSizeMb { get; set; }
    [JsonPropertyName("max_pages")] public int? MaxPages { get; set; }
    [JsonPropertyName("system_prompt")] public string? SystemPrompt { get; set; }
    [JsonPropertyName("extraction_prompt")] public string? ExtractionPrompt { get; set; }
    [JsonPropertyName("json_schema")] public string? JsonSchema { get; set; }
    [JsonPropertyName("validation_rules")] public List<ValidationRule>? ValidationRules { get; set; }
}
public class DocumentTypePrompts
{
    [JsonPropertyName("type_id")] public string TypeId { get; set; } = "";
    [JsonPropertyName("system_prompt")] public string SystemPrompt { get; set; } = "";
    [JsonPropertyName("extraction_prompt")] public string ExtractionPrompt { get; set; } = "";
    [JsonPropertyName("json_schema")] public string JsonSchema { get; set; } = "";
    [JsonPropertyName("validation_rules")] public List<ValidationRule> ValidationRules { get; set; } = [];
}
public class DocumentTypeSummary
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("accepted_file_types")] public List<string> AcceptedFileTypes { get; set; } = [];
    [JsonPropertyName("max_file_size_mb")] public int MaxFileSizeMb { get; set; }
    [JsonPropertyName("max_page_count")] public int MaxPageCount { get; set; }
    [JsonPropertyName("icon_name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IconName { get; set; }
    [JsonPropertyName("category")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Category { get; set; }
    [JsonPropertyName("supports_batch")] public bool SupportsBatch { get; set; }
    [JsonPropertyName("supports_excel_export")] public bool SupportsExcelExport { get; set; }
    [JsonPropertyName("supports_json_export")] public bool SupportsJsonExport { get; set; }
    [JsonPropertyName("dual_pass_enabled")] public bool DualPassEnabled { get; set; }
    [JsonPropertyName("sample_fields")] public List<string> SampleFields { get; set; } = [];
}
public class DocumentTypeDetail
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("display_name")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("accepted_file_types")] public List<string> AcceptedFileTypes { get; set; } = [];
    [JsonPropertyName("max_file_size_mb")] public int MaxFileSizeMb { get; set; }
    [JsonPropertyName("max_page_count")] public int MaxPageCount { get; set; }
    [JsonPropertyName("icon_name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? IconName { get; set; }
    [JsonPropertyName("category")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Category { get; set; }
    [JsonPropertyName("supports_batch")] public bool SupportsBatch { get; set; }
    [JsonPropertyName("supports_excel_export")] public bool SupportsExcelExport { get; set; }
    [JsonPropertyName("supports_json_export")] public bool SupportsJsonExport { get; set; }
    [JsonPropertyName("extraction_settings")] public DocumentExtractionSettings ExtractionSettings { get; set; } = new();
    [JsonPropertyName("dual_pass")] public DualPassConfig DualPass { get; set; } = new();
    [JsonPropertyName("output")] public DocumentOutputConfig Output { get; set; } = new();
    [JsonPropertyName("validation_rule_count")] public int ValidationRuleCount { get; set; }
    [JsonPropertyName("sample_fields")] public List<string> SampleFields { get; set; } = [];
}
public class ReloadResult { public int TypesBefore { get; set; } public int TypesAfter { get; set; } public List<string> Types { get; set; } = []; }
