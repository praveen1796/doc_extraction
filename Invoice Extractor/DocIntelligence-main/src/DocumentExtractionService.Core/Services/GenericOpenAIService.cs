using Azure;
using Azure.AI.OpenAI;
using DocumentExtractionService.Core.Configuration;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using System.ClientModel;
using System.Diagnostics;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Generic Azure OpenAI extraction service.
///
/// KEY DESIGN: Unlike the original console app which had hardcoded prompts,
/// this service accepts prompts and schema at runtime — making it fully
/// generic. The document type registry provides the right prompts for each
/// document type.
///
/// ACCURACY PRINCIPLES PRESERVED FROM V6.0:
/// 1. Structured JSON output via JSON Schema enforcement
/// 2. Images sent BEFORE text (model sees visual layout first)
/// 3. Reasoning models (GPT-5.2): use reasoning_effort=medium (NOT temperature)
/// 4. Dual-pass verification with full system prompt
/// 5. Reasoning effort minimum "medium" enforced
///
/// GPT-5.2 REASONING MODEL NOTES:
/// - temperature is NOT SUPPORTED → causes HTTP 400 if set
/// - max_tokens is NOT SUPPORTED → use max_completion_tokens
/// - reasoning_effort defaults to "none" → must explicitly set to "medium"
/// - Context window: 400K tokens, max output: 128K tokens
/// - Supports structured output (JSON Schema)
///
/// FIX v1.2:
/// - Added NetworkTimeout (10 min) to AzureOpenAIClient to prevent HTTP timeouts
/// - Added TaskCanceledException catch for HTTP-level timeouts (distinct from user cancellation)
/// - Added per-request CancellationTokenSource with configurable timeout
/// - Better logging for timeout vs API errors
/// </summary>
public interface IGenericOpenAIService
{
    Task<OpenAiExtractionResult> ExtractAsync(
        PdfContent pdfContent,
        string systemPrompt,
        string extractionPromptTemplate,
        string jsonSchema,
        DocumentExtractionSettings settings,
        CancellationToken cancellationToken = default);

    Task<string?> RunDualPassAsync(
        PdfContent pdfContent,
        string systemPrompt,
        string jsonSchema,
        string firstPassJson,
        List<string> criticalFields,
        decimal confidenceThreshold,
        DocumentExtractionSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Text Q+A over extracted JSON only (no PDF). Used by extraction chat MVP.
    /// </summary>
    Task<string> ChatOverExtractedJsonAsync(
        string documentType,
        string sourceFileName,
        string extractionJson,
        string userMessage,
        CancellationToken cancellationToken = default);
}

public class GenericOpenAIService : IGenericOpenAIService
{
    private readonly AzureOpenAISettings _settings;
    private readonly ILogger<GenericOpenAIService> _logger;
    private readonly AzureOpenAIClient _azureClient;

    public GenericOpenAIService(
        IOptions<AppSettings> settings,
        ILogger<GenericOpenAIService> logger)
    {
        _settings = settings.Value.AzureOpenAI;
        _logger = logger;

        // ══════════════════════════════════════════════════════════════════
        //  FIX v1.2: Configure HTTP timeout on the Azure OpenAI client.
        //
        //  The default HttpClient timeout is 100 seconds, which is far too
        //  short for well plan extraction with multiple high-res images.
        //  A 136-page well plan can take 3-8 minutes for GPT to process.
        //
        //  NetworkTimeout controls the HttpClient.Timeout value used by
        //  the underlying transport pipeline.
        // ══════════════════════════════════════════════════════════════════
        var networkTimeout = TimeSpan.FromMinutes(_settings.TimeoutMinutes > 0
            ? _settings.TimeoutMinutes
            : 10);
        var clientOptions = new AzureOpenAIClientOptions
        {
            NetworkTimeout = networkTimeout
        };

        _azureClient = new AzureOpenAIClient(
            new Uri(_settings.Endpoint),
            new AzureKeyCredential(_settings.ApiKey),
            clientOptions);

        _logger.LogInformation("Azure OpenAI client initialized: endpoint={Endpoint}, " +
            "deployment={Deployment}, timeout={Timeout}min, maxRetries={MaxRetries}",
            _settings.Endpoint, _settings.DeploymentName,
            networkTimeout.TotalMinutes, _settings.MaxRetries);
    }

    /// <summary>
    /// Primary extraction pass. Uses the document type's prompts and schema.
    /// </summary>
    public async Task<OpenAiExtractionResult> ExtractAsync(
        PdfContent pdfContent,
        string systemPrompt,
        string extractionPromptTemplate,
        string jsonSchema,
        DocumentExtractionSettings settings,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var chatClient = _azureClient.GetChatClient(_settings.DeploymentName);

        // ── Build response format ──
        var responseFormat = BuildResponseFormat(jsonSchema, pdfContent.FileName);

        // ── Build extraction prompt from template ──
        var userPrompt = BuildPromptFromTemplate(extractionPromptTemplate, pdfContent);

        // ── Build message content ──
        // ORDER: Instructions → Images (visual layout first) → Text
        var userContentParts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(userPrompt)
        };

        if (pdfContent.PageImages.Count > 0)
        {
            userContentParts.Add(ChatMessageContentPart.CreateTextPart(
                $"\n[PAGE IMAGES: {pdfContent.PageImages.Count} page(s) — examine these for visual layout, logos, tables]\n"));
        }

        foreach (var imageBytes in pdfContent.PageImages)
        {
            var imageData = BinaryData.FromBytes(imageBytes);
            userContentParts.Add(ChatMessageContentPart.CreateImagePart(
                imageData, "image/png", ChatImageDetailLevel.High));
        }

        if (!string.IsNullOrWhiteSpace(pdfContent.ExtractedText))
        {
            userContentParts.Add(ChatMessageContentPart.CreateTextPart(
                "\n\n=== EXTRACTED TEXT (verify against images) ===\n" +
                pdfContent.ExtractedText +
                "\n=== END OF EXTRACTED TEXT ==="));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userContentParts)
        };

        var options = BuildCompletionOptions(settings, responseFormat);

        _logger.LogInformation("  Sending to GPT: {ImageCount} images, ~{TextLen}K text chars, " +
            "maxTokens={MaxTokens}, effort={Effort} for {File}",
            pdfContent.PageImages.Count,
            (pdfContent.ExtractedText?.Length ?? 0) / 1000,
            settings.MaxTokens,
            settings.ReasoningEffort,
            pdfContent.FileName);

        // ── Call with retry logic ──
        string resultJson = "";
        string lastInvalidResponse = "";  // FIX: Track invalid responses for error reporting
        int totalTokens = 0;
        int attempt = 0;
        Exception? lastException = null;

        while (attempt < _settings.MaxRetries)
        {
            attempt++;
            try
            {
                _logger.LogDebug("  API call attempt {Attempt}/{Max} for {File}",
                    attempt, _settings.MaxRetries, pdfContent.FileName);

                var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
                var completion = response.Value;
                resultJson = completion.Content[0].Text;

                // FIX API-3: Null-safe token count access
                totalTokens = completion.Usage?.TotalTokenCount ?? 0;

                _logger.LogInformation("  GPT response: {Tokens} tokens, finish={Reason}, elapsed={Ms}ms",
                    totalTokens, completion.FinishReason, sw.ElapsedMilliseconds);

                // ══════════════════════════════════════════════════════════════
                //  FIX API-5 / OUTPUT-1: Detect output token truncation.
                //
                //  When maxTokens is too low, FinishReason = "length" and the
                //  JSON is truncated mid-output. Retrying is pointless — same
                //  limit will hit again. Report a specific error instead.
                // ══════════════════════════════════════════════════════════════
                var finishReason = completion.FinishReason.ToString()?.ToLowerInvariant() ?? "";
                if (finishReason == "length" || finishReason == "Length")
                {
                    _logger.LogError("  ❌ Output TRUNCATED — FinishReason='length'. " +
                        "maxTokens ({MaxTokens}) is too low for this document ({Wells} wells). " +
                        "Response ends with: ...{Tail}",
                        settings.MaxTokens,
                        pdfContent.FileName,
                        resultJson.Length > 200 ? resultJson[^200..] : resultJson);

                    throw new InvalidOperationException(
                        $"Output token limit exceeded for {pdfContent.FileName}. " +
                        $"The model produced {totalTokens} tokens but was capped at maxTokens={settings.MaxTokens}. " +
                        $"The document likely has too many wells or formation tops for the current token budget. " +
                        $"Increase maxTokens in the well_plan config.json (current: {settings.MaxTokens}, " +
                        $"recommended: {Math.Max(settings.MaxTokens * 2, 65536)}).");
                }

                if (!IsValidJson(resultJson))
                {
                    lastInvalidResponse = resultJson;  // FIX API-4: Track for error message
                    _logger.LogWarning("  Response is not valid JSON on attempt {Attempt} " +
                        "(length={Len}, starts with: {Head}...)",
                        attempt, resultJson.Length,
                        resultJson.Length > 100 ? resultJson[..100] : resultJson);
                    continue;
                }

                break;
            }
            // ══════════════════════════════════════════════════════════════
            //  FIX API-1: Catch Azure Content Safety filter rejections.
            //
            //  Drilling documents contain terms like "H2S contingency",
            //  "kill fluid", "BOP failure", "explosion risk" that Azure
            //  Content Safety may flag. This returns HTTP 400 with an
            //  error code of "content_filter". The retry loop previously
            //  only caught 429 and 500+ — 400 fell through to the
            //  generic catch which gave up with an unhelpful error.
            // ══════════════════════════════════════════════════════════════
            catch (ClientResultException ex) when (ex.Status == 400)
            {
                var errorBody = ex.Message ?? "";
                var isContentFilter = errorBody.Contains("content_filter", StringComparison.OrdinalIgnoreCase)
                    || errorBody.Contains("ContentFilter", StringComparison.OrdinalIgnoreCase)
                    || errorBody.Contains("ResponsibleAIPolicyViolation", StringComparison.OrdinalIgnoreCase);

                if (isContentFilter)
                {
                    _logger.LogWarning("  ⚠️ Azure Content Safety filter triggered for {File}. " +
                        "Drilling safety terminology may have been flagged. " +
                        "Error: {Error}",
                        pdfContent.FileName, errorBody.Length > 500 ? errorBody[..500] : errorBody);

                    // On first content filter hit, try stripping safety/ERP sections from text
                    if (attempt == 1 && !string.IsNullOrEmpty(pdfContent.ExtractedText))
                    {
                        _logger.LogInformation("  Retrying with safety sections stripped from text...");
                        pdfContent.ExtractedText = StripSafetySections(pdfContent.ExtractedText);
                        // Rebuild messages with cleaned text
                        var cleanedParts = RebuildContentParts(userPrompt, pdfContent);
                        messages = new List<ChatMessage>
                        {
                            new SystemChatMessage(systemPrompt),
                            new UserChatMessage(cleanedParts)
                        };
                        lastException = ex;
                        continue;
                    }

                    lastException = ex;
                    // Don't retry further — content filter will keep triggering
                    throw new InvalidOperationException(
                        $"Azure Content Safety filter rejected the document {pdfContent.FileName}. " +
                        $"The document contains drilling safety terminology that was flagged. " +
                        $"Consider processing with fewer vision pages or using the async endpoint. " +
                        $"Filter details: {(errorBody.Length > 300 ? errorBody[..300] : errorBody)}", ex);
                }
                else
                {
                    // Non-content-filter 400 error (e.g., schema validation failure)
                    _logger.LogError(ex, "  Azure OpenAI rejected request (400): {Error}",
                        errorBody.Length > 500 ? errorBody[..500] : errorBody);
                    lastException = ex;
                    if (attempt < _settings.MaxRetries)
                        await Task.Delay(_settings.RetryDelayMs, cancellationToken);
                }
            }
            catch (ClientResultException ex) when (ex.Status == 429)
            {
                var delay = _settings.RetryDelayMs * attempt;
                _logger.LogWarning("  Rate limited by Azure OpenAI (429), waiting {Delay}ms...", delay);
                lastException = ex;
                await Task.Delay(delay, cancellationToken);
            }
            catch (ClientResultException ex) when (ex.Status >= 500)
            {
                _logger.LogWarning(ex, "  Azure OpenAI server error ({Status}), retrying...", ex.Status);
                lastException = ex;
                await Task.Delay(_settings.RetryDelayMs, cancellationToken);
            }
            // ══════════════════════════════════════════════════════════════
            //  FIX v1.2: Handle HTTP-level timeouts separately.
            //
            //  TaskCanceledException is thrown when the HttpClient times out.
            //  This is different from user-initiated cancellation (which has
            //  cancellationToken.IsCancellationRequested == true).
            //
            //  For HTTP timeouts, we retry with exponential backoff since the
            //  model may still be processing — the request may succeed on
            //  retry if the model finishes faster or if transient network
            //  issues resolve.
            // ══════════════════════════════════════════════════════════════
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                var delay = _settings.RetryDelayMs * attempt * 2; // Longer backoff for timeouts
                _logger.LogWarning(
                    "  HTTP timeout on attempt {Attempt}/{Max} for {File} (elapsed {Elapsed}ms). " +
                    "Retrying in {Delay}ms...",
                    attempt, _settings.MaxRetries, pdfContent.FileName,
                    sw.ElapsedMilliseconds, delay);
                lastException = ex;

                if (attempt < _settings.MaxRetries)
                    await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // User/system cancelled the request — don't retry
                _logger.LogWarning("  Extraction cancelled by caller for {File}", pdfContent.FileName);
                throw;
            }
            catch (Exception ex) when (attempt < _settings.MaxRetries)
            {
                _logger.LogWarning(ex, "  Extraction failed on attempt {Attempt}: {Error}",
                    attempt, ex.Message);
                lastException = ex;
                await Task.Delay(_settings.RetryDelayMs, cancellationToken);
            }
        }

        if (string.IsNullOrWhiteSpace(resultJson))
        {
            var errorMsg = $"Failed to extract data after {_settings.MaxRetries} attempts " +
                $"for {pdfContent.FileName} (elapsed {sw.ElapsedMilliseconds}ms)";

            if (lastException != null)
                errorMsg += $". Last error: {lastException.GetType().Name}: {lastException.Message}";

            // FIX API-4: Include last invalid JSON response for debugging
            if (!string.IsNullOrEmpty(lastInvalidResponse))
            {
                var preview = lastInvalidResponse.Length > 300
                    ? lastInvalidResponse[..300] + "..."
                    : lastInvalidResponse;
                errorMsg += $". Last GPT response was invalid JSON ({lastInvalidResponse.Length} chars): {preview}";
            }

            throw new InvalidOperationException(errorMsg, lastException);
        }

        sw.Stop();
        return new OpenAiExtractionResult
        {
            JsonResult = resultJson,
            TokensUsed = totalTokens,
            ElapsedMs = sw.ElapsedMilliseconds
        };
    }

    /// <summary>
    /// Dual-pass verification. Uses a text-search strategy to find missed/low-confidence fields.
    /// Preserves the original "smart dual-pass" approach from v6.0.
    /// </summary>
    public async Task<string?> RunDualPassAsync(
        PdfContent pdfContent,
        string systemPrompt,
        string jsonSchema,
        string firstPassJson,
        List<string> criticalFields,
        decimal confidenceThreshold,
        DocumentExtractionSettings settings,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("  Running dual-pass verification for {File}", pdfContent.FileName);

        var chatClient = _azureClient.GetChatClient(_settings.DeploymentName);

        // Identify problem fields from first pass
        var problems = IdentifyProblems(firstPassJson, criticalFields, confidenceThreshold);
        bool hasProblems = problems.Count > 0;

        _logger.LogInformation("  Dual-pass: {Count} fields need attention", problems.Count);

        // Build text-search prompt
        var prompt = BuildDualPassPrompt(pdfContent, firstPassJson, problems, hasProblems);

        var contentParts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(prompt)
        };

        // Add page images for visual re-check
        foreach (var imageBytes in pdfContent.PageImages)
        {
            var imageData = BinaryData.FromBytes(imageBytes);
            contentParts.Add(ChatMessageContentPart.CreateImagePart(
                imageData, "image/png", ChatImageDetailLevel.High));
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(contentParts)
        };

        var responseFormat = BuildResponseFormat(jsonSchema, pdfContent.FileName);
        var options = BuildCompletionOptions(settings, responseFormat);

        try
        {
            var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
            var completion = response.Value;
            var correctedJson = completion.Content[0].Text;

            _logger.LogInformation("  Dual-pass complete: {Tokens} tokens", completion.Usage.TotalTokenCount);

            return IsValidJson(correctedJson) ? correctedJson : null;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("  Dual-pass HTTP timeout for {File}: {Error}",
                pdfContent.FileName, ex.Message);
            return null; // Fall back to first pass result
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "  Dual-pass failed for {File}", pdfContent.FileName);
            return null; // Fall back to first pass result
        }
    }

    /// <inheritdoc />
    public async Task<string> ChatOverExtractedJsonAsync(
        string documentType,
        string sourceFileName,
        string extractionJson,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            throw new ArgumentException("Message is required.", nameof(userMessage));

        var chatClient = _azureClient.GetChatClient(_settings.DeploymentName);

        const string systemPrompt = """
You are an assistant that answers questions using ONLY the structured JSON extraction in the user message.
Rules:
- If the answer is not in the JSON (or cannot be clearly inferred), say it is not present in the extracted data.
- Do not invent fields, numbers, dates, or facts.
- Use concise markdown: **bold** for key values, bullet lists, small tables when helpful.
""";

        var userBody = $"""
## Document type
{documentType}

## Source file
{sourceFileName}

## Extracted data (JSON)
```json
{extractionJson}
```

## Question
{userMessage}
""";

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userBody)
        };

        var chatSettings = new DocumentExtractionSettings
        {
            MaxTokens = 4096,
            ReasoningEffort = "medium",
            Temperature = 0.2f
        };

        var options = BuildCompletionOptionsForPlainText(chatSettings);
        var response = await chatClient.CompleteChatAsync(messages, options, cancellationToken);
        var completion = response.Value;
        _logger.LogInformation("  Chat over JSON: {Tokens} tokens, finish={Reason}",
            completion.Usage?.TotalTokenCount ?? 0, completion.FinishReason);
        return completion.Content[0].Text ?? "";
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Chat completion without JSON-schema response format (plain assistant text).</summary>
    private ChatCompletionOptions BuildCompletionOptionsForPlainText(DocumentExtractionSettings settings)
    {
        var options = new ChatCompletionOptions();
        if (!IsReasoningModel())
        {
            options.Temperature = settings.Temperature;
        }

        SetMaxCompletionTokens(options, settings.MaxTokens);
        SetReasoningEffort(options, settings.ReasoningEffort);
        return options;
    }

    private static List<string> IdentifyProblems(
        string firstPassJson,
        List<string> criticalFields,
        decimal threshold)
    {
        var problems = new List<string>();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(firstPassJson);
            var root = doc.RootElement;

            foreach (var field in criticalFields)
            {
                if (root.TryGetProperty(field, out var value))
                {
                    bool isEmpty = value.ValueKind == System.Text.Json.JsonValueKind.Null ||
                                   (value.ValueKind == System.Text.Json.JsonValueKind.String &&
                                    string.IsNullOrWhiteSpace(value.GetString()));

                    if (isEmpty)
                    {
                        problems.Add($"{field}: (empty)");
                    }
                }
                else
                {
                    problems.Add($"{field}: (missing)");
                }
            }

            // Check confidence scores
            if (root.TryGetProperty("confidence", out var confidence))
            {
                foreach (var confField in confidence.EnumerateObject())
                {
                    if (confField.Value.ValueKind == System.Text.Json.JsonValueKind.Number)
                    {
                        var score = confField.Value.GetDecimal();
                        if (score < threshold && criticalFields.Contains(confField.Name))
                        {
                            var existing = problems.FirstOrDefault(p => p.StartsWith(confField.Name));
                            if (existing == null)
                            {
                                problems.Add($"{confField.Name}: low confidence ({score:F2})");
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // JSON parse error — treat all critical fields as problems
            return criticalFields.Select(f => $"{f}: (parse error)").ToList();
        }

        return problems;
    }

    private static string BuildDualPassPrompt(
        PdfContent pdfContent,
        string firstPassJson,
        List<string> problems,
        bool hasProblems)
    {
        var sb = new System.Text.StringBuilder();

        if (hasProblems)
        {
            sb.AppendLine("SECOND-PASS EXTRACTION: The first pass returned EMPTY or LOW-CONFIDENCE values.");
            sb.AppendLine("\nPROBLEM FIELDS:");
            foreach (var p in problems)
                sb.AppendLine($"  ❌ {p}");
            sb.AppendLine("\nYOUR TASK: Search using a DIFFERENT approach.");
            sb.AppendLine("1. Read the EXTRACTED TEXT LINE BY LINE — search for labels and values the image scan missed");
            sb.AppendLine("2. Check the page images again — look at TOP, BOTTOM, FOOTER, any PAYMENT STUBS");
            sb.AppendLine($"3. Check the filename: \"{pdfContent.FileName}\"");
        }
        else
        {
            sb.AppendLine("VERIFICATION PASS: Confirm the first-pass extraction is correct.");
        }

        sb.AppendLine("\nFIRST-PASS RESULT:");
        sb.AppendLine(firstPassJson);

        if (!string.IsNullOrWhiteSpace(pdfContent.ExtractedText))
        {
            sb.AppendLine("\n=== EXTRACTED TEXT (search line-by-line) ===");
            var lines = pdfContent.ExtractedText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd();
                if (!string.IsNullOrWhiteSpace(line))
                    sb.AppendLine($"L{i + 1}: {line}");
            }
            sb.AppendLine("=== END OF EXTRACTED TEXT ===");
        }

        sb.AppendLine("\nReturn the COMPLETE corrected JSON. ALL dates MUST be YYYY-MM-DD.");

        return sb.ToString();
    }

    private string BuildPromptFromTemplate(string template, PdfContent pdfContent)
    {
        return template
            .Replace("{{FILE_NAME}}", pdfContent.FileName)
            .Replace("{{PAGE_COUNT}}", pdfContent.PageCount.ToString())
            .Replace("{{IS_SCANNED}}", pdfContent.IsScanned ? "Yes (scanned)" : "No (text available)")
            .Replace("{{EXTRACTION_METHOD}}", pdfContent.ExtractionMethod)
            .Replace("{{FILE_SIZE_KB}}", (pdfContent.FileSize / 1024).ToString());
    }

    private ChatResponseFormat BuildResponseFormat(string jsonSchema, string fileName)
    {
        try
        {
            var format = ChatResponseFormat.CreateJsonSchemaFormat(
                "document_extraction",
                BinaryData.FromString(jsonSchema),
                jsonSchemaIsStrict: true);
            _logger.LogDebug("  Using strict JSON Schema response format for {File}", fileName);
            return format;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("  JSON Schema format unavailable ({Error}), using basic JSON", ex.Message);
            return ChatResponseFormat.CreateJsonObjectFormat();
        }
    }

    private ChatCompletionOptions BuildCompletionOptions(
        DocumentExtractionSettings settings,
        ChatResponseFormat responseFormat)
    {
        var options = new ChatCompletionOptions
        {
            ResponseFormat = responseFormat,
        };

        // ══════════════════════════════════════════════════════════════
        //  CRITICAL FIX: GPT-5 series are REASONING MODELS.
        //  Reasoning models do NOT support temperature, top_p,
        //  presence_penalty, frequency_penalty, or max_tokens.
        //
        //  Setting Temperature = 0 causes:
        //  HTTP 400: "Unsupported value: 'temperature' does not
        //  support 0 with this model. Only the default (1) value
        //  is supported."
        //
        //  This was the ROOT CAUSE of "getting something different"
        //  errors — every API call returned 400 before even
        //  processing the document.
        //
        //  For reasoning models, use reasoning_effort instead of
        //  temperature to control output quality.
        // ══════════════════════════════════════════════════════════════
        if (!IsReasoningModel())
        {
            options.Temperature = settings.Temperature;
            _logger.LogDebug("  Using temperature={Temp} (non-reasoning model)", settings.Temperature);
        }
        else
        {
            _logger.LogDebug("  Skipping temperature (reasoning model: {Model}). " +
                "Using reasoning_effort={Effort} instead.",
                _settings.DeploymentName, settings.ReasoningEffort);
        }

        // Set max_completion_tokens via reflection (SDK compatibility)
        SetMaxCompletionTokens(options, settings.MaxTokens);

        // Set reasoning effort (minimum "medium")
        SetReasoningEffort(options, settings.ReasoningEffort);

        return options;
    }

    /// <summary>
    /// Detect if the configured model is a reasoning model (GPT-5 series, o-series).
    /// Reasoning models do not support temperature, top_p, etc.
    /// </summary>
    private bool IsReasoningModel()
    {
        var model = _settings.DeploymentName?.ToLowerInvariant() ?? "";
        return model.Contains("gpt-5") ||
               model.Contains("o1") ||
               model.Contains("o3") ||
               model.Contains("o4") ||
               model.StartsWith("o-");
    }

    private void SetMaxCompletionTokens(ChatCompletionOptions options, int maxTokens)
    {
        try
        {
            var field = typeof(ChatCompletionOptions).GetField(
                "_serializedAdditionalRawData",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (field != null)
            {
                var dict = field.GetValue(options) as IDictionary<string, BinaryData>
                    ?? new Dictionary<string, BinaryData>();
                dict["max_completion_tokens"] = BinaryData.FromObjectAsJson(maxTokens);
                field.SetValue(options, dict);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not set max_completion_tokens: {Error}", ex.Message);
        }
    }

    private void SetReasoningEffort(ChatCompletionOptions options, string effort)
    {
        var normalizedEffort = effort?.ToLowerInvariant() ?? "medium";

        // For document extraction, "none" and "low" produce poor results
        if (normalizedEffort == "none" || normalizedEffort == "low")
        {
            _logger.LogWarning("⚠️ reasoning_effort='{Effort}' overridden to 'medium' for extraction accuracy",
                normalizedEffort);
            normalizedEffort = "medium";
        }

        bool setViaReflection = false;

        // Try SDK property first
        try
        {
            var prop = options.GetType().GetProperty("ReasoningEffort");
            if (prop != null)
            {
                var reasoningType = typeof(ChatCompletionOptions).Assembly
                    .GetType("OpenAI.Chat.ChatReasoningEffort");
                if (reasoningType != null)
                {
                    var effortValue = normalizedEffort switch
                    {
                        "high" or "xhigh" => reasoningType.GetProperty("High")?.GetValue(null),
                        _ => reasoningType.GetProperty("Medium")?.GetValue(null)
                    };

                    if (effortValue != null)
                    {
                        prop.SetValue(options, effortValue);
                        setViaReflection = true;
                        _logger.LogDebug("  Set reasoning_effort='{Effort}' via SDK property", normalizedEffort);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Could not set ReasoningEffort via SDK property: {Error}", ex.Message);
        }

        // ══════════════════════════════════════════════════════════════
        //  FALLBACK: Set reasoning_effort via raw data dictionary.
        //
        //  If the SDK version doesn't have ChatReasoningEffort type,
        //  the reflection above fails silently. For GPT-5.2, this means
        //  reasoning_effort defaults to "none" on the API side — the
        //  model won't reason at all and produces poor extraction.
        //
        //  This fallback ensures reasoning_effort is always sent.
        // ══════════════════════════════════════════════════════════════
        if (!setViaReflection)
        {
            try
            {
                var field = typeof(ChatCompletionOptions).GetField(
                    "_serializedAdditionalRawData",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

                if (field != null)
                {
                    var dict = field.GetValue(options) as IDictionary<string, BinaryData>
                        ?? new Dictionary<string, BinaryData>();
                    dict["reasoning_effort"] = BinaryData.FromObjectAsJson(normalizedEffort);
                    field.SetValue(options, dict);
                    _logger.LogDebug("  Set reasoning_effort='{Effort}' via raw data fallback", normalizedEffort);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not set reasoning_effort at all: {Error}. " +
                    "GPT-5.2 defaults to 'none' — extraction quality will be degraded.", ex.Message);
            }
        }
    }

    private static bool IsValidJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();
        if (!text.StartsWith("{") && !text.StartsWith("[")) return false;
        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(text);
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Strip safety/ERP sections from extracted text to avoid content filter triggers.
    /// Drilling programs often contain Emergency Response Plans with terms that
    /// Azure Content Safety flags (H2S contingency, explosion risk, etc.).
    /// </summary>
    private static string StripSafetySections(string text)
    {
        var lines = text.Split('\n');
        var filtered = new System.Text.StringBuilder();
        bool inSafetySection = false;

        foreach (var line in lines)
        {
            var upper = line.ToUpperInvariant();

            // Detect start of safety sections
            if (upper.Contains("EMERGENCY RESPONSE") || upper.Contains("H2S CONTINGENCY") ||
                upper.Contains("EVACUATION PLAN") || upper.Contains("SAFETY DRILL") ||
                upper.Contains("HAZARDOUS GAS") || upper.Contains("BLOWOUT SCENARIO") ||
                upper.Contains("NEAREST HOSPITAL") || upper.Contains("FIRE FIGHTING"))
            {
                inSafetySection = true;
                filtered.AppendLine("[SAFETY SECTION REMOVED FOR CONTENT COMPLIANCE]");
                continue;
            }

            // Detect end of safety section (next page marker or major section header)
            if (inSafetySection && (upper.Contains("[PAGE ") || upper.Contains("CASING DESIGN") ||
                upper.Contains("DRILLING PROGRAM") || upper.Contains("WELL SCHEMATIC") ||
                upper.Contains("FORMATION") || upper.Contains("BHA ")))
            {
                inSafetySection = false;
            }

            if (!inSafetySection)
            {
                filtered.AppendLine(line);
            }
        }

        return filtered.ToString();
    }

    /// <summary>
    /// Rebuild message content parts with updated text (after safety stripping).
    /// </summary>
    private List<ChatMessageContentPart> RebuildContentParts(string userPrompt, PdfContent pdfContent)
    {
        var parts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(userPrompt)
        };

        if (pdfContent.PageImages.Count > 0)
        {
            parts.Add(ChatMessageContentPart.CreateTextPart(
                $"\n[PAGE IMAGES: {pdfContent.PageImages.Count} page(s)]\n"));
        }

        foreach (var imageBytes in pdfContent.PageImages)
        {
            var imageData = BinaryData.FromBytes(imageBytes);
            parts.Add(ChatMessageContentPart.CreateImagePart(
                imageData, "image/png", ChatImageDetailLevel.High));
        }

        if (!string.IsNullOrWhiteSpace(pdfContent.ExtractedText))
        {
            parts.Add(ChatMessageContentPart.CreateTextPart(
                "\n\n=== EXTRACTED TEXT (verify against images) ===\n" +
                pdfContent.ExtractedText +
                "\n=== END OF EXTRACTED TEXT ==="));
        }

        return parts;
    }
}
