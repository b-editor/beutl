namespace Beutl.AgentToolkit.Installation;

public enum AgentInstallScope
{
    Project,
    Global,
}

/// <summary>
/// A JSON MCP config file the installer can merge servers into.
/// Agents whose MCP config is not a mergeable JSON file (TOML, YAML,
/// non-standard server shapes, UI-managed) have no location and require
/// manual registration.
/// </summary>
public sealed record AgentMcpLocation(string ConfigFileName, string ServersPropertyName);

/// <summary>
/// Install conventions for one AI coding agent. Directory values are
/// relative to the project folder (project scope) or to the user profile
/// (global scope); null means the agent has no convention for that item.
/// The skills paths follow the vercel-labs/skills compatibility table.
/// </summary>
public sealed record AgentDefinition(
    string Id,
    string DisplayName,
    string ProjectSkillsDirectory,
    string GlobalSkillsDirectory,
    string? ProjectSubagentsDirectory = null,
    string? GlobalSubagentsDirectory = null,
    AgentMcpLocation? ProjectMcp = null,
    AgentMcpLocation? GlobalMcp = null)
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
    private static readonly AgentMcpLocation s_mcpServersJson = new(".mcp.json", "mcpServers");

    public static IReadOnlyList<AgentDefinition> Agents { get; } =
    [
        new("claude-code", "Claude Code",
            ProjectSkillsDirectory: Path.Combine(".claude", "skills"),
            GlobalSkillsDirectory: Path.Combine(".claude", "skills"),
            ProjectSubagentsDirectory: Path.Combine(".claude", "agents"),
            GlobalSubagentsDirectory: Path.Combine(".claude", "agents"),
            ProjectMcp: s_mcpServersJson,
            GlobalMcp: new AgentMcpLocation(".claude.json", "mcpServers")),
        new("codex", "Codex",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".codex", "skills")),
        new("opencode", "OpenCode",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "opencode", "skills")),
        new("cursor", "Cursor",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".cursor", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".cursor", "mcp.json"), "mcpServers"),
            GlobalMcp: new AgentMcpLocation(Path.Combine(".cursor", "mcp.json"), "mcpServers")),
        new("github-copilot", "GitHub Copilot",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".copilot", "skills"),
            ProjectMcp: new AgentMcpLocation(Path.Combine(".vscode", "mcp.json"), "servers")),
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
            GlobalSkillsDirectory: Path.Combine(".agents", "skills")),
        new("amp", "Amp",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "agents", "skills"),
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
        new("openhands", "OpenHands",
            ProjectSkillsDirectory: Path.Combine(".openhands", "skills"),
            GlobalSkillsDirectory: Path.Combine(".openhands", "skills")),
        new("crush", "Crush",
            ProjectSkillsDirectory: Path.Combine(".crush", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "crush", "skills")),
        new("droid", "Droid (Factory)",
            ProjectSkillsDirectory: Path.Combine(".factory", "skills"),
            GlobalSkillsDirectory: Path.Combine(".factory", "skills")),
        new("trae", "Trae",
            ProjectSkillsDirectory: Path.Combine(".trae", "skills"),
            GlobalSkillsDirectory: Path.Combine(".trae", "skills")),
        new("junie", "Junie",
            ProjectSkillsDirectory: Path.Combine(".junie", "skills"),
            GlobalSkillsDirectory: Path.Combine(".junie", "skills")),
        new("universal", "Universal (.agents)",
            ProjectSkillsDirectory: Path.Combine(".agents", "skills"),
            GlobalSkillsDirectory: Path.Combine(".config", "agents", "skills"),
            ProjectMcp: s_mcpServersJson),
    ];

    public static AgentDefinition? Find(string id)
    {
        return Agents.FirstOrDefault(a => a.Id == id);
    }
}
