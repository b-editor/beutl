using System.Text.RegularExpressions;

namespace Beutl.Agents;

public enum AgentDiagnosticKind { CliMissing, AuthRequired, OutdatedCli, RateLimited, McpUnavailable, Generic }

public sealed record AgentDiagnostic(AgentDiagnosticKind Kind, AgentProviderDescriptor? Provider,
    string? RawMessage, string? Command = null, string? CliVersion = null, Version? MinimumVersion = null);

public static partial class AgentDiagnosticClassifier
{
    public static AgentDiagnostic? FromProbe(AgentProviderProbeResult probe)
    {
        if (!probe.IsAvailable)
            return Create(AgentDiagnosticKind.CliMissing, probe.Provider, probe.Error);
        return probe.IsOutdated
            ? Create(AgentDiagnosticKind.OutdatedCli, probe.Provider, probe.ExecutablePath) with
            {
                CliVersion = probe.CliVersion,
                MinimumVersion = probe.MinimumVersion
            }
            : null;
    }

    public static AgentDiagnostic FromFailure(string error, AgentProviderDescriptor? provider, AgentDiagnosticKind? kind = null)
        => Create(kind ?? Classify(error), provider, error);

    private static AgentDiagnosticKind Classify(string error)
    {
        bool HasAny(params string[] patterns) => patterns.Any(pattern => error.Contains(pattern, StringComparison.OrdinalIgnoreCase));

        if (HasAny("beutl mcp launcher", "beutl mcp endpoint", "live mcp did not start", "live mcp endpoint", "mcp server 'beutl' failed"))
            return AgentDiagnosticKind.McpUnavailable;
        if (HasAny("could not find an executable for", "executable not found", "command not found"))
            return AgentDiagnosticKind.CliMissing;
        if (HasAny("unsupported cli version", "claude code version is too old", "update claude code", "upgrade claude code"))
            return AgentDiagnosticKind.OutdatedCli;
        if (RateLimitCode().IsMatch(error) || HasAny("rate limit", "rate_limit", "rate-limit", "ratelimit", "quota", "too many requests",
            "usage limit", "usage_limit"))
            return AgentDiagnosticKind.RateLimited;
        if (UnauthorizedCode().IsMatch(error) || HasAny("not logged in", "not signed in", "authentication required",
            "authentication_error", "authentication failed", "authentication expired", "login required", "please run /login",
            "please sign in", "please log in", "missing api key", "api key is missing", "api key is not set",
            "invalid api key", "invalid_api_key", "invalid x-api-key", "token expired", "token has expired", "token_expired",
            "credentials have expired", "access token could not be refreshed")
            || HasAny("missing environment variable", "required environment variable") && HasAny("_api_key"))
            return AgentDiagnosticKind.AuthRequired;
        return AgentDiagnosticKind.Generic;
    }

    private static AgentDiagnostic Create(AgentDiagnosticKind kind, AgentProviderDescriptor? provider, string? message)
    {
        string? command = (kind, provider?.Kind) switch
        {
            (AgentDiagnosticKind.CliMissing, AgentProviderKind.ClaudeCode) => "npm install -g @anthropic-ai/claude-code",
            (AgentDiagnosticKind.CliMissing, AgentProviderKind.Codex) => "npm install -g @openai/codex",
            (AgentDiagnosticKind.AuthRequired, AgentProviderKind.ClaudeCode) => "claude auth login",
            (AgentDiagnosticKind.AuthRequired, AgentProviderKind.Codex) => "codex login",
            (AgentDiagnosticKind.OutdatedCli, AgentProviderKind.ClaudeCode) => "claude update",
            (AgentDiagnosticKind.OutdatedCli, AgentProviderKind.Codex) => "npm install -g @openai/codex@latest",
            _ => null
        };
        return new(kind, provider, message, command);
    }

    [GeneratedRegex(@"\b429\b", RegexOptions.CultureInvariant)]
    private static partial Regex RateLimitCode();

    [GeneratedRegex(@"\b401\b|\bunauthorized\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex UnauthorizedCode();
}
