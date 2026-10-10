namespace Beutl.AgentToolkit.Installation;

public enum AgentInstallScope
{
    Project,
    Global,
}

/// <summary>
/// An MCP config file the installer can merge servers into.
/// Agents whose MCP config is not a supported file format (YAML,
/// non-standard server shapes, app-managed storage) have no location and
/// require manual registration.
/// <para>
/// <paramref name="StdioTypeValue"/>: value of the "type" key on the Beutl
/// server entry, a stdio launcher, or null to omit it (most agents infer stdio
/// from "command").
/// </para>
/// </summary>
public sealed record AgentMcpLocation(
    string ConfigFileName,
    string ServersPropertyName,
    string? StdioTypeValue = null,
    McpConfigFormat Format = McpConfigFormat.Json);

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
        new(".mcp.json", "mcpServers");

    private static readonly AgentMcpLocation s_codexMcpToml =
        new(Path.Combine(".codex", "config.toml"), "mcp_servers", Format: McpConfigFormat.CodexToml);

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
        // Write TOML directly so installs work without requiring the Codex CLI.
        new("codex", "Codex",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".agents", "skills"),
            ProjectSubagentsDirectory: Path.Combine(".codex", "agents"),
            GlobalSubagentsDirectory: Path.Combine(".codex", "agents"),
            ProjectMcp: s_codexMcpToml,
            GlobalMcp: s_codexMcpToml,
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
            ProjectMcp: new AgentMcpLocation(".mcp.json", "mcpServers", StdioTypeValue: "stdio"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".copilot", "mcp-config.json"), "mcpServers", StdioTypeValue: "stdio")),
        new("gemini-cli", "Gemini CLI",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".gemini", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".gemini", "settings.json"), "mcpServers"),
            GlobalMcp: new AgentMcpLocation(Path.Combine(".gemini", "settings.json"), "mcpServers")),
        new("windsurf", "Windsurf",
            ProjectSkillsDirectory: Path.Combine(".windsurf", "skills"),
            GlobalSkillsDirectory: Path.Combine(".codeium", "windsurf", "skills"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".codeium", "windsurf", "mcp_config.json"), "mcpServers")),
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
            ProjectMcp: new AgentMcpLocation(Path.Combine(".roo", "mcp.json"), "mcpServers")),
        new("kilo", "Kilo Code",
            ProjectSkillsDirectory: Path.Combine(".kilocode", "skills"),
            GlobalSkillsDirectory: Path.Combine(".kilocode", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".kilocode", "mcp.json"), "mcpServers")),
        new("continue", "Continue",
            ProjectSkillsDirectory: Path.Combine(".continue", "skills"),
            GlobalSkillsDirectory: Path.Combine(".continue", "skills")),
        new("qwen-code", "Qwen Code",
            ProjectSkillsDirectory: Path.Combine(".qwen", "skills"),
            GlobalSkillsDirectory: Path.Combine(".qwen", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".qwen", "settings.json"), "mcpServers"),
            GlobalMcp: new AgentMcpLocation(Path.Combine(".qwen", "settings.json"), "mcpServers")),
        // ~/.openhands/mcp.json documents stdio entries, which both Beutl entries are.
        new("openhands", "OpenHands",
            ProjectSkillsDirectory: Path.Combine(".openhands", "skills"),
            GlobalSkillsDirectory: Path.Combine(".openhands", "skills"),
            GlobalMcp: new AgentMcpLocation(Path.Combine(".openhands", "mcp.json"), "mcpServers")),
        // Crush requires an explicit "type" on every entry.
        new("crush", "Crush",
            ProjectSkillsDirectory: Path.Combine(".crush", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "crush", "skills"),
            ProjectMcp: new AgentMcpLocation(".crush.json", "mcp", StdioTypeValue: "stdio"),
            GlobalMcp: new AgentMcpLocation(
                Path.Combine(".config", "crush", "crush.json"), "mcp", StdioTypeValue: "stdio")),
        new("droid", "Droid (Factory)",
            ProjectSkillsDirectory: Path.Combine(".factory", "skills"),
            GlobalSkillsDirectory: Path.Combine(".factory", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".factory", "mcp.json"), "mcpServers"),
            GlobalMcp: new AgentMcpLocation(Path.Combine(".factory", "mcp.json"), "mcpServers")),
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
