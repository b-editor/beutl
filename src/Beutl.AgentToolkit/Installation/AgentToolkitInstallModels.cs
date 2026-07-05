namespace Beutl.AgentToolkit.Installation;

public enum AgentToolkitAssetKind
{
    Skill,
    Subagent,
}

public sealed record AgentToolkitAsset(
    AgentToolkitAssetKind Kind,
    string RelativePath,
    string Content);

public sealed record AgentToolkitInstallOptions
{
    public required string AgentRoot { get; init; }

    public string SkillsDirectory { get; init; } = "skills";

    public string SubagentsDirectory { get; init; } = "agents";

    public bool InstallSkills { get; init; } = true;

    public bool InstallSubagents { get; init; } = true;

    public bool InstallStdioMcp { get; init; } = true;

    public bool InstallLiveMcp { get; init; }

    public string McpConfigFileName { get; init; } = ".mcp.json";

    public string McpServersPropertyName { get; init; } = "mcpServers";

    // null omits the "type" key; most agents infer stdio from "command".
    public string? StdioMcpTypeValue { get; init; }

    public string LiveMcpUrlPropertyName { get; init; } = "url";

    // null omits the "type" key on the live (remote) entry.
    public string? LiveMcpTypeValue { get; init; } = "http";

    public string StdioMcpServerName { get; init; } = "beutl-agent";

    public string LiveMcpServerName { get; init; } = "beutl-live";

    public string? WorkspaceRoot { get; init; }

    public string? StdioMcpCommand { get; init; }

    public IReadOnlyList<string> StdioMcpArguments { get; init; } = [];

    public IReadOnlyDictionary<string, string> StdioMcpEnvironment { get; init; }
        = new Dictionary<string, string>();

    public Uri? LiveMcpUri { get; init; }
}

public sealed record AgentToolkitInstallResult(
    IReadOnlyList<string> InstalledFiles,
    string? McpConfigPath,
    bool InstalledStdioMcp,
    bool InstalledLiveMcp);

public sealed record AgentToolkitMcpServerCommand(
    string Command,
    IReadOnlyList<string> Arguments,
    string Source);
