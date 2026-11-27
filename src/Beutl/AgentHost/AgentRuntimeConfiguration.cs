using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Agents;
using Beutl.Configuration;

namespace Beutl.AgentHost;

internal static class AgentRuntimeConfiguration
{
    private static readonly JsonSerializerOptions s_providerJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static AgentPermissionMode ParsePermissionMode(string value)
        => Enum.TryParse(value, ignoreCase: true, out AgentPermissionMode mode) && Enum.IsDefined(mode)
            ? mode
            : value?.Trim() switch
            {
                string s when s.Equals("Ask", StringComparison.OrdinalIgnoreCase) => AgentPermissionMode.Supervised,
                string s when s.Equals("FullAuto", StringComparison.OrdinalIgnoreCase) => AgentPermissionMode.FullAccess,
                _ => AgentPermissionMode.Auto
            };

    public static IReadOnlyList<AgentCustomProviderConfig?> ReadCustomProviders(string json)
        => string.IsNullOrWhiteSpace(json) ? [] : JsonSerializer.Deserialize<AgentCustomProviderConfig?[]>(json, s_providerJson) ?? [];

    public static string WriteCustomProviders(IEnumerable<AgentCustomProviderConfig?> providers)
        => JsonSerializer.Serialize(providers, s_providerJson);

    public static IReadOnlyList<AgentProviderDescriptor> GetProviders(
        AiAgentConfig config, IReadOnlyList<AgentCustomProviderConfig?> customProviders)
    {
        List<AgentProviderDescriptor> providers =
        [
            WithCommand(AgentProviderDescriptor.ClaudeCode, config.ClaudeCodeExecutablePath.Value),
            WithCommand(AgentProviderDescriptor.Codex, config.CodexExecutablePath.Value)
        ];
        var ids = providers.Select(provider => provider.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var custom in customProviders)
        {
            if (custom is null || string.IsNullOrWhiteSpace(custom.Id) || string.IsNullOrWhiteSpace(custom.DisplayName)
                || string.IsNullOrWhiteSpace(custom.Command) || !ids.Add(custom.Id.Trim()))
                continue;

            providers.Add(custom.ToDescriptor() with
            {
                Id = custom.Id.Trim(),
                DisplayName = custom.DisplayName.Trim(),
                Command = custom.Command.Trim()
            });
        }
        return providers;
    }

    private static AgentProviderDescriptor WithCommand(AgentProviderDescriptor provider, string path)
        => string.IsNullOrWhiteSpace(path) ? provider : provider with { Command = path.Trim() };
}
