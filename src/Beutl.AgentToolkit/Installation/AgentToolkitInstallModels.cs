namespace Beutl.AgentToolkit.Installation;

public enum AgentToolkitAssetKind
{
    Skill,
    Subagent,
}

public enum SubagentFileFormat
{
    Markdown,
    CodexToml,
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

    public SubagentFileFormat SubagentFormat { get; init; } = SubagentFileFormat.Markdown;

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

    public const string DefaultStdioServerName = "beutl-agent";

    public const string DefaultLiveServerName = "beutl-live";

    public string StdioMcpServerName { get; init; } = DefaultStdioServerName;

    public string LiveMcpServerName { get; init; } = DefaultLiveServerName;

    public string? WorkspaceRoot { get; init; }

    public string? StdioMcpCommand { get; init; }

    public IReadOnlyList<string> StdioMcpArguments { get; init; } = [];

    public IReadOnlyDictionary<string, string> StdioMcpEnvironment { get; init; }
        = new Dictionary<string, string>();

    public Uri? LiveMcpUri { get; init; }

    // Written as the remote entry's "headers" object; carries the
    // Authorization bearer token, which never travels in the URL.
    public IReadOnlyDictionary<string, string> LiveMcpHeaders { get; init; }
        = new Dictionary<string, string>();
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
