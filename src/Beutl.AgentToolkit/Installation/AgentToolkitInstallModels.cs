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

public enum McpConfigFormat
{
    Json,
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

    // One stdio server covers both ways of working: it edits a running Beutl editor live whenever
    // a call names an instanceId, and project files headlessly otherwise.
    public bool InstallMcp { get; init; } = true;

    public string McpConfigFileName { get; init; } = ".mcp.json";

    public string? McpConfigRoot { get; init; }

    public McpConfigFormat McpConfigFormat { get; init; }

    public string McpServersPropertyName { get; init; } = "mcpServers";

    // null omits the "type" key; most agents infer stdio from "command".
    public string? McpTypeValue { get; init; }

    public const string DefaultServerName = "beutl-agent";

    public string McpServerName { get; init; } = DefaultServerName;

    // Written as BEUTL_WORKSPACE: the write boundary of headless project edits.
    public string? WorkspaceRoot { get; init; }

    public string? McpCommand { get; init; }

    public IReadOnlyList<string> McpArguments { get; init; } = [];

    // Typically BEUTL_HOME, so the server discovers the editors of the same profile as the
    // installing Beutl; the agent configuration never carries the live MCP token or a URL.
    public IReadOnlyDictionary<string, string> McpEnvironment { get; init; }
        = new Dictionary<string, string>();
}

public sealed record AgentToolkitInstallResult(
    IReadOnlyList<string> InstalledFiles,
    string? McpConfigPath,
    bool InstalledMcp,
    IReadOnlyList<InstalledFileRecord> AssetFileRecords);

public sealed record AgentToolkitMcpServerCommand(
    string Command,
    IReadOnlyList<string> Arguments,
    string Source);
