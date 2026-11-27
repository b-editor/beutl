namespace Beutl.Agents;

using System.Text.Json;
using System.Text.Json.Serialization;

public enum AgentProviderKind { ClaudeCode, Codex, Acp }

public sealed record AgentProviderDescriptor(
    string Id,
    AgentProviderKind Kind,
    string DisplayName,
    string Command,
    IReadOnlyList<string>? Arguments = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<AgentPermissionMode, string>? PermissionModes = null)
{
    public static AgentProviderDescriptor ClaudeCode { get; } = new("claude", AgentProviderKind.ClaudeCode, "Claude Code", "claude");
    public static AgentProviderDescriptor Codex { get; } = new("codex", AgentProviderKind.Codex, "Codex", "codex");
}

public sealed record AgentCustomProviderConfig(
    string Id,
    string DisplayName,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string>? Environment = null,
    IReadOnlyDictionary<AgentPermissionMode, string>? PermissionModes = null)
{
    public AgentProviderDescriptor ToDescriptor()
        => new(Id, AgentProviderKind.Acp, DisplayName, Command, Arguments, Environment, PermissionModes);
}

public sealed record AgentProviderProbeResult(
    AgentProviderDescriptor Provider, string? ExecutablePath, string? SearchPath, string? Error, string? CliVersion = null)
{
    private static readonly Version s_minimumClaudeCodeVersion = new(2, 1, 0);

    public bool IsAvailable => ExecutablePath != null;
    public Version? MinimumVersion => Provider.Kind == AgentProviderKind.ClaudeCode ? s_minimumClaudeCodeVersion : null;
    public bool IsOutdated
    {
        get
        {
            if (MinimumVersion is not { } minimum || CliVersion is not { } version)
                return false;
            var core = version.AsSpan();
            int suffix = core.IndexOfAny('-', '+');
            bool prerelease = suffix >= 0 && core[suffix] == '-';
            return Version.TryParse(suffix < 0 ? core : core[..suffix], out var current)
                && (current < minimum || current == minimum && prerelease);
        }
    }
}

public enum AgentPermissionMode { Supervised, AutoAcceptEdits, Auto, FullAccess }
public enum AgentPermissionDecision { AllowOnce, AllowAlways, RejectOnce, RejectAlways }

public sealed record AgentPermissionOption(AgentPermissionDecision Decision, string Label);

public sealed record AgentPermissionRequest(
    string RequestId,
    Guid ThreadId,
    Guid TurnId,
    string ToolName,
    string? McpServerName = null,
    bool IsReadOnly = false,
    string? Description = null,
    IReadOnlyList<AgentPermissionOption>? Options = null);

[JsonConverter(typeof(AgentQuestionOptionConverter))]
public sealed record AgentQuestionOption(string Label, string? Description = null);

public sealed record AgentQuestion(
    string Id, string Text, IReadOnlyList<AgentQuestionOption>? Options = null, bool IsSecret = false,
    string? Header = null, bool MultiSelect = false, bool IsOther = true);

public sealed record AgentUserQuestionRequest(
    string RequestId, Guid ThreadId, Guid TurnId, IReadOnlyList<AgentQuestion> Questions);

internal sealed class AgentQuestionOptionConverter : JsonConverter<AgentQuestionOption>
{
    // Earlier timeline entries stored the option label alone.
    public override AgentQuestionOption Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new AgentQuestionOption(reader.GetString()!);

        var option = JsonSerializer.Deserialize<Option>(ref reader, options);
        return option?.Label is { } label
            ? new AgentQuestionOption(label, option.Description)
            : throw new JsonException("Expected a question option label.");
    }

    public override void Write(Utf8JsonWriter writer, AgentQuestionOption value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, new Option(value.Label, value.Description), options);

    private sealed record Option(string Label, string? Description);
}
