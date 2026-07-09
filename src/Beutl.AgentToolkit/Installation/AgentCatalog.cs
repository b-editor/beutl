namespace Beutl.AgentToolkit.Installation;

public enum AgentInstallScope
{
    Project,
    Global,
}

/// <summary>
/// A JSON MCP config file the installer can merge servers into.
/// Agents whose MCP config is not a mergeable JSON file (TOML, YAML,
/// non-standard server shapes, app-managed storage) have no location and
/// require manual registration.
/// <para>
/// <paramref name="StdioTypeValue"/>: value of the stdio entry's "type" key,
/// or null to omit it (most agents infer stdio from "command").
/// <paramref name="RemoteUrlPropertyName"/>: property carrying a remote
/// (HTTP) server's URL, or null when the agent's remote-entry shape is
/// unknown/unsupported — the live MCP entry is then not written.
/// <paramref name="RemoteTypeValue"/>: value of the remote entry's "type"
/// key, or null to omit it.
/// </para>
/// </summary>
public sealed record AgentMcpLocation(
    string ConfigFileName,
    string ServersPropertyName,
    string? StdioTypeValue = null,
    string? RemoteUrlPropertyName = "url",
    string? RemoteTypeValue = null);

/// <summary>
/// Install conventions for one AI coding agent. Directory values are
/// relative to the project folder (project scope) or to the user profile
/// (global scope); null means the agent has no convention for that item.
/// The skills paths follow the vercel-labs/skills compatibility table and
/// each vendor's official docs (verified 2026-07).
/// </summary>
public sealed record AgentDefinition(
    string Id,
    string DisplayName,
    string ProjectSkillsDirectory,
    string GlobalSkillsDirectory,
    string? ProjectSubagentsDirectory = null,
    string? GlobalSubagentsDirectory = null,
    AgentMcpLocation? ProjectMcp = null,
    AgentMcpLocation? GlobalMcp = null,
    SubagentFileFormat SubagentFormat = SubagentFileFormat.Markdown)
{
    public string SkillsDirectory(AgentInstallScope scope)
        => scope == AgentInstallScope.Project ? ProjectSkillsDirectory : GlobalSkillsDirectory;

    public string? SubagentsDirectory(AgentInstallScope scope)
        => scope == AgentInstallScope.Project ? ProjectSubagentsDirectory : GlobalSubagentsDirectory;

    public AgentMcpLocation? Mcp(AgentInstallScope scope)
        => scope == AgentInstallScope.Project ? ProjectMcp : GlobalMcp;
}

public static class AgentCatalog
{
    private static readonly AgentMcpLocation s_repoRootMcpJson =
        new(".mcp.json", "mcpServers", RemoteTypeValue: "http");

    public static IReadOnlyList<AgentDefinition> Agents { get; } =
    [
        // Global MCP deliberately absent: ~/.claude.json is Claude Code's live
        // state file (projects, history, OAuth) — use `claude mcp add --scope user`.
        new("claude-code", "Claude Code",
            ProjectSkillsDirectory: Path.Combine(".claude", "skills"),
            GlobalSkillsDirectory: Path.Combine(".claude", "skills"),
            ProjectSubagentsDirectory: Path.Combine(".claude", "agents"),
            GlobalSubagentsDirectory: Path.Combine(".claude", "agents"),
            ProjectMcp: s_repoRootMcpJson),
        // Codex MCP config is TOML (~/.codex/config.toml) — registration goes
        // through `codex mcp add` (AgentMcpCliCommands) instead of a file merge.
        // Subagents are TOML and get converted at install time.
        new("codex", "Codex",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".agents", "skills"),
            ProjectSubagentsDirectory: Path.Combine(".codex", "agents"),
            GlobalSubagentsDirectory: Path.Combine(".codex", "agents"),
            SubagentFormat: SubagentFileFormat.CodexToml),
        new("opencode", "OpenCode",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "opencode", "skills")),
        new("cursor", "Cursor",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".cursor", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".cursor", "mcp.json"), "mcpServers"),
            GlobalMcp: new AgentMcpLocation(Path.Combine(".cursor", "mcp.json"), "mcpServers")),
        // Copilot CLI conventions: repo-root .mcp.json / ~/.copilot/mcp-config.json;
        // "stdio" is the cross-client type name its docs recommend.
        new("github-copilot", "GitHub Copilot",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".copilot", "skills"),
            ProjectMcp: new AgentMcpLocation(
                ".mcp.json", "mcpServers", StdioTypeValue: "stdio", RemoteTypeValue: "http"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".copilot", "mcp-config.json"), "mcpServers",
                StdioTypeValue: "stdio", RemoteTypeValue: "http")),
        new("gemini-cli", "Gemini CLI",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".gemini", "skills"),
            ProjectMcp: new AgentMcpLocation(
                Path.Combine(".gemini", "settings.json"), "mcpServers", RemoteUrlPropertyName: "httpUrl"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".gemini", "settings.json"), "mcpServers", RemoteUrlPropertyName: "httpUrl")),
        new("windsurf", "Windsurf",
            ProjectSkillsDirectory: Path.Combine(".windsurf", "skills"),
            GlobalSkillsDirectory: Path.Combine(".codeium", "windsurf", "skills"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".codeium", "windsurf", "mcp_config.json"), "mcpServers",
                RemoteUrlPropertyName: "serverUrl")),
        new("cline", "Cline",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".agents", "skills")),
        new("zed", "Zed",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".agents", "skills")),
        new("warp", "Warp",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".agents", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".warp", ".mcp.json"), "mcpServers"),
            GlobalMcp: new AgentMcpLocation(Path.Combine(".warp", ".mcp.json"), "mcpServers")),
        new("amp", "Amp",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "agents", "skills"),
            ProjectMcp: new AgentMcpLocation(
                Path.Combine(".amp", "settings.json"), "amp.mcpServers"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".config", "amp", "settings.json"), "amp.mcpServers")),
        new("goose", "Goose",
            ProjectSkillsDirectory: Path.Combine(".goose", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "goose", "skills")),
        new("hermes-agent", "Hermes Agent",
            ProjectSkillsDirectory: Path.Combine(".hermes", "skills"),
            GlobalSkillsDirectory: Path.Combine(".hermes", "skills")),
        new("roo", "Roo Code",
            ProjectSkillsDirectory: Path.Combine(".roo", "skills"),
            GlobalSkillsDirectory: Path.Combine(".roo", "skills"),
            ProjectMcp: new AgentMcpLocation(
                Path.Combine(".roo", "mcp.json"), "mcpServers", RemoteTypeValue: "streamable-http")),
        new("kilo", "Kilo Code",
            ProjectSkillsDirectory: Path.Combine(".kilocode", "skills"),
            GlobalSkillsDirectory: Path.Combine(".kilocode", "skills"),
            ProjectMcp: new AgentMcpLocation(
                Path.Combine(".kilocode", "mcp.json"), "mcpServers", RemoteTypeValue: "streamable-http")),
        new("continue", "Continue",
            ProjectSkillsDirectory: Path.Combine(".continue", "skills"),
            GlobalSkillsDirectory: Path.Combine(".continue", "skills")),
        new("qwen-code", "Qwen Code",
            ProjectSkillsDirectory: Path.Combine(".qwen", "skills"),
            GlobalSkillsDirectory: Path.Combine(".qwen", "skills"),
            ProjectMcp: new AgentMcpLocation(
                Path.Combine(".qwen", "settings.json"), "mcpServers", RemoteUrlPropertyName: "httpUrl"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".qwen", "settings.json"), "mcpServers", RemoteUrlPropertyName: "httpUrl")),
        // ~/.openhands/mcp.json documents stdio entries only, so the live
        // (HTTP) entry stays manual.
        new("openhands", "OpenHands",
            ProjectSkillsDirectory: Path.Combine(".openhands", "skills"),
            GlobalSkillsDirectory: Path.Combine(".openhands", "skills"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".openhands", "mcp.json"), "mcpServers", RemoteUrlPropertyName: null)),
        // Crush requires an explicit "type" on every entry.
        new("crush", "Crush",
            ProjectSkillsDirectory: Path.Combine(".crush", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "crush", "skills"),
            ProjectMcp: new AgentMcpLocation(
                ".crush.json", "mcp", StdioTypeValue: "stdio", RemoteTypeValue: "http"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".config", "crush", "crush.json"), "mcp",
                StdioTypeValue: "stdio", RemoteTypeValue: "http")),
        new("droid", "Droid (Factory)",
            ProjectSkillsDirectory: Path.Combine(".factory", "skills"),
            GlobalSkillsDirectory: Path.Combine(".factory", "skills"),
            ProjectMcp: new AgentMcpLocation(
                Path.Combine(".factory", "mcp.json"), "mcpServers", RemoteTypeValue: "http"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".factory", "mcp.json"), "mcpServers", RemoteTypeValue: "http")),
        new("trae", "Trae",
            ProjectSkillsDirectory: Path.Combine(".trae", "skills"),
            GlobalSkillsDirectory: Path.Combine(".trae", "skills")),
        new("junie", "Junie",
            ProjectSkillsDirectory: Path.Combine(".junie", "skills"),
            GlobalSkillsDirectory: Path.Combine(".junie", "skills"),
            ProjectMcp: new AgentMcpLocation(
                Path.Combine(".junie", "mcp", "mcp.json"), "mcpServers"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".junie", "mcp", "mcp.json"), "mcpServers")),
        new("universal", "Universal (.agents)",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "agents", "skills"),
            ProjectMcp: s_repoRootMcpJson),
    ];

    public static AgentDefinition? Find(string id)
    {
        return Agents.FirstOrDefault(a => a.Id == id);
    }
}
