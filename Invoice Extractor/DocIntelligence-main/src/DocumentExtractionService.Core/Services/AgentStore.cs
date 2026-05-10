using System.Collections.Concurrent;
using System.Text.Json;
using DocumentExtractionService.Core.Configuration;
using DocumentExtractionService.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocumentExtractionService.Core.Services;

/// <summary>
/// Agent Store — registry for agent definitions.
///
/// TWO SOURCES OF AGENTS:
///   1. Explicit agents: loaded from Agents/{agentId}/agent.json
///   2. Implicit agents: auto-synthesized from DocumentTypes/ (every doc type is an agent)
///
/// Implicit agents have ID prefix "doc_" (e.g., "doc_invoice").
/// Explicit agents can override an implicit agent by referencing the same document_type_id.
/// </summary>
public interface IAgentStore
{
    AgentDefinition? GetAgent(string agentId);
    IReadOnlyList<AgentDefinition> GetAllAgents();
    Task ReloadAsync(CancellationToken cancellationToken = default);
}

public class AgentStore : IAgentStore
{
    private readonly IDocumentTypeRegistry _docTypeRegistry;
    private readonly ILogger<AgentStore> _logger;
    private readonly ConcurrentDictionary<string, AgentDefinition> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _agentsFolder;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public AgentStore(
        IDocumentTypeRegistry docTypeRegistry,
        IOptions<AppSettings> settings,
        ILogger<AgentStore> logger)
    {
        _docTypeRegistry = docTypeRegistry;
        _logger = logger;

        // Agents folder lives alongside DocumentTypes folder
        var docTypesRoot = docTypeRegistry.GetRootFolder();
        _agentsFolder = Path.Combine(Path.GetDirectoryName(docTypesRoot) ?? "", "Agents");
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        _agents.Clear();
        var loaded = 0;

        // ── Step 1: Synthesize agents from document types ──
        foreach (var docType in _docTypeRegistry.GetAllDocumentTypes())
        {
            var agent = AgentDefinition.FromDocumentType(docType);
            _agents[agent.Id] = agent;
            loaded++;
        }
        _logger.LogInformation("Synthesized {Count} agents from document types", loaded);

        // ── Step 2: Load explicit agent definitions (override implicit ones) ──
        if (Directory.Exists(_agentsFolder))
        {
            foreach (var dir in Directory.GetDirectories(_agentsFolder))
            {
                if (cancellationToken.IsCancellationRequested) break;
                var agentId = Path.GetFileName(dir).ToLowerInvariant();
                try
                {
                    var agentFile = Path.Combine(dir, "agent.json");
                    if (!File.Exists(agentFile))
                    {
                        _logger.LogWarning("Skipping {AgentId}: agent.json not found", agentId);
                        continue;
                    }

                    var json = await File.ReadAllTextAsync(agentFile, cancellationToken);
                    var agent = JsonSerializer.Deserialize<AgentDefinition>(json, JsonOptions);
                    if (agent == null) continue;

                    agent.Id = agentId;
                    agent.FolderPath = dir;

                    // Resolve document type reference
                    if (!string.IsNullOrEmpty(agent.DocumentTypeId))
                    {
                        agent.DocumentType = _docTypeRegistry.GetDocumentType(agent.DocumentTypeId);
                    }

                    // Load optional agent-level prompts
                    var reasoningPromptFile = Path.Combine(dir, "reasoning_prompt.txt");
                    if (File.Exists(reasoningPromptFile))
                        agent.ReasoningPrompt = await File.ReadAllTextAsync(reasoningPromptFile, cancellationToken);

                    var systemPromptFile = Path.Combine(dir, "agent_system_prompt.txt");
                    if (File.Exists(systemPromptFile))
                        agent.AgentSystemPrompt = await File.ReadAllTextAsync(systemPromptFile, cancellationToken);

                    _agents[agent.Id] = agent;
                    loaded++;
                    _logger.LogInformation("✓ Loaded explicit agent: {AgentId} ({DisplayName})", agentId, agent.DisplayName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load agent from: {Dir}", dir);
                }
            }
        }
        else
        {
            Directory.CreateDirectory(_agentsFolder);
            _logger.LogInformation("Created Agents folder: {Folder}", _agentsFolder);
        }

        _logger.LogInformation("Agent store loaded: {Count} total agents", _agents.Count);
    }

    public AgentDefinition? GetAgent(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId)) return null;
        return _agents.TryGetValue(agentId, out var agent) ? agent : null;
    }

    public IReadOnlyList<AgentDefinition> GetAllAgents()
        => _agents.Values.Where(a => a.Enabled).OrderBy(a => a.DisplayName).ToList();
}
