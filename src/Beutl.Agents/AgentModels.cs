namespace Beutl.Agents;

public sealed record AgentModelOption(string? Id, string? Effort);

public sealed record AgentModelDescriptor(
    string Id,
    string DisplayName,
    string ShortName,
    string ProviderId,
    IReadOnlyList<string> ReasoningEfforts,
    string? DefaultReasoningEffort = null,
    bool IsSecondary = false,
    bool IsProviderDefault = false,
    string? ResolvedModelId = null,
    long? ContextWindowTokens = null);

public sealed record AgentModelListRequest(
    string ProviderId,
    AgentProviderKind Provider,
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    Guid? ThreadId = null);

public static class AgentModelCatalog
{
    private static readonly string[] s_codexEfforts = ["low", "medium", "high", "xhigh"];
    private static readonly string[] s_claudeEfforts = ["low", "medium", "high", "xhigh", "max"];
    private static readonly AgentModelDescriptor[] s_codexModels =
    [
        new("gpt-6.2-sol", "GPT-6.2 Sol", "6.2 Sol", "codex", [.. s_codexEfforts, "max", "ultra"]),
        new("gpt-6.2-astra", "GPT-6.2 Astra", "6.2 Astra", "codex", [.. s_codexEfforts, "max", "ultra"]),
        new("gpt-5.4", "GPT-5.4", "5.4", "codex", s_codexEfforts),
        new("gpt-5.3-codex", "GPT-5.3 Codex", "5.3 Codex", "codex", s_codexEfforts),
        new("gpt-5.4-mini", "GPT-5.4 Mini", "5.4 Mini", "codex", s_codexEfforts, IsSecondary: true)
    ];
    private static readonly AgentModelDescriptor[] s_claudeModels =
    [
        new("opus", "Claude Opus", "Opus", "claude", s_claudeEfforts, ContextWindowTokens: 200_000),
        new("sonnet", "Claude Sonnet", "Sonnet", "claude", ["low", "medium", "high"], ContextWindowTokens: 200_000),
        new("haiku", "Claude Haiku", "Haiku", "claude", [], ContextWindowTokens: 200_000)
    ];

    public static IReadOnlyList<AgentModelDescriptor> GetFallback(AgentProviderDescriptor provider)
    {
        var models = provider.Kind switch
        {
            AgentProviderKind.Codex => s_codexModels,
            AgentProviderKind.ClaudeCode => s_claudeModels,
            _ => []
        };
        return models.Select(model => model with { ProviderId = provider.Id }).ToArray();
    }
}
