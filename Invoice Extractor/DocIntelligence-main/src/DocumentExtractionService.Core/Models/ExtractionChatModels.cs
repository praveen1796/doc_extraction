using System.Text.Json.Serialization;

namespace DocumentExtractionService.Core.Models;

/// <summary>POST /api/v1/extraction/chat — ask questions over extracted JSON only (MVP).</summary>
public class ExtractionChatRequest
{
    /// <summary>Look up extraction from server store (after a real API extraction).</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; set; }

    /// <summary>Required user question.</summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    /// <summary>Optional inline extraction payload when <see cref="RequestId"/> is not in store (e.g. demo UI).</summary>
    [JsonPropertyName("data")]
    public System.Text.Json.JsonElement? Data { get; set; }

    [JsonPropertyName("document_type")]
    public string? DocumentType { get; set; }

    [JsonPropertyName("source_file")]
    public string? SourceFile { get; set; }
}

public class ExtractionChatResponse
{
    [JsonPropertyName("reply")]
    public string Reply { get; set; } = "";
}
