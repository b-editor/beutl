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

    // Earlier versions installed a second, URL-based entry under this name that carried the live
    // MCP bearer token. Reinstalling removes it from the agent configuration.
    public const string LegacyLiveServerName = "beutl-live";

    public string? McpCommand { get; init; }

    public IReadOnlyList<string> McpArguments { get; init; } = [];

    // Typically BEUTL_HOME, so the server discovers the editors of the same profile as the
    // installing Beutl; the agent configuration never carries the live MCP token or a URL.
    public IReadOnlyDictionary<string, string> McpEnvironment { get; init; }
        = new Dictionary<string, string>();

    // The files an earlier install wrote, with the hashes of what it wrote (from the install manifest).
    // A skill or subagent file whose content no longer matches, or that no install recorded, is backed
    // up before the bundled version replaces it.
    public IReadOnlyList<InstalledFileRecord> PreviousFiles { get; init; } = [];
}

public sealed record AgentToolkitInstallResult(
    IReadOnlyList<string> InstalledFiles,
    bool InstalledMcp,
    IReadOnlyList<InstalledFileRecord> AssetFileRecords)
{
    // Set when the JSON MCP configuration has comments and needed a change: writing it would drop the
    // comments, so it was left unchanged and the user has to make the edit.
    public ManualMcpEdit? ManualMcpEdit { get; init; }

    // Skill and subagent files the user had edited, copied aside before the bundled version replaced them.
    public IReadOnlyList<BackedUpFile> BackedUpFiles { get; init; } = [];
}

// Snippet is a JSON object that shows where the entry goes; ObsoleteEntries are the server entries of
// earlier versions to remove from the file.
public sealed record ManualMcpEdit(string ConfigPath, string Snippet, IReadOnlyList<string> ObsoleteEntries);

public sealed record BackedUpFile(string Path, string BackupPath);

public sealed record AgentToolkitMcpServerCommand(
    string Command,
    IReadOnlyList<string> Arguments);
