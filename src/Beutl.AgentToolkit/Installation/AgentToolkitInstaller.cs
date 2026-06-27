using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Beutl.AgentToolkit.Installation;

public static class AgentToolkitInstaller
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static async Task<AgentToolkitInstallResult> InstallAsync(
        AgentToolkitInstallOptions options,
        IEnumerable<AgentToolkitAsset> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(assets);

        string agentRoot = EnsureDirectory(options.AgentRoot);
        var installedFiles = new List<string>();

        foreach (AgentToolkitAsset asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((asset.Kind == AgentToolkitAssetKind.Skill && !options.InstallSkills)
                || (asset.Kind == AgentToolkitAssetKind.Subagent && !options.InstallSubagents))
            {
                continue;
            }

            string targetPath = GetAssetTargetPath(agentRoot, options, asset);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllTextAsync(targetPath, asset.Content, cancellationToken).ConfigureAwait(false);
            installedFiles.Add(targetPath);
        }

        string? mcpConfigPath = null;
        if (options.InstallStdioMcp || options.InstallLiveMcp)
        {
            mcpConfigPath = GetSafeTargetPath(agentRoot, options.McpConfigFileName);
            await WriteMcpConfigAsync(mcpConfigPath, options, cancellationToken).ConfigureAwait(false);
            installedFiles.Add(mcpConfigPath);
        }

        return new AgentToolkitInstallResult(
            installedFiles,
            mcpConfigPath,
            options.InstallStdioMcp,
            options.InstallLiveMcp);
    }

    private static async Task WriteMcpConfigAsync(
        string path,
        AgentToolkitInstallOptions options,
        CancellationToken cancellationToken)
    {
        JsonObject root = await ReadConfigRootAsync(path, cancellationToken).ConfigureAwait(false);
        JsonObject servers = GetOrCreateObject(root, options.McpServersPropertyName);

        if (options.InstallStdioMcp)
        {
            servers[options.StdioMcpServerName] = CreateStdioServer(options);
        }

        if (options.InstallLiveMcp)
        {
            if (options.LiveMcpUri is null)
            {
                throw new InvalidOperationException("Live MCP installation requires a live MCP URI.");
            }

            servers[options.LiveMcpServerName] = new JsonObject
            {
                ["type"] = "http",
                ["url"] = options.LiveMcpUri.ToString(),
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string json = root.ToJsonString(s_jsonOptions);
        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private static JsonObject CreateStdioServer(AgentToolkitInstallOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.StdioMcpCommand))
        {
            throw new InvalidOperationException("Stdio MCP installation requires a command.");
        }

        var args = new JsonArray();
        foreach (string arg in options.StdioMcpArguments)
        {
            args.Add(arg);
        }

        var server = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = options.StdioMcpCommand,
            ["args"] = args,
        };

        var env = new JsonObject();
        if (!string.IsNullOrWhiteSpace(options.WorkspaceRoot))
        {
            env["BEUTL_WORKSPACE"] = Path.GetFullPath(options.WorkspaceRoot);
        }

        foreach (KeyValuePair<string, string> pair in options.StdioMcpEnvironment)
        {
            env[pair.Key] = pair.Value;
        }

        if (env.Count > 0)
        {
            server["env"] = env;
        }

        return server;
    }

    private static async Task<JsonObject> ReadConfigRootAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        string json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        JsonNode? node = JsonNode.Parse(
            json,
            documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

        return node as JsonObject
               ?? throw new InvalidDataException($"MCP config must be a JSON object: {path}");
    }

    private static JsonObject GetOrCreateObject(JsonObject parent, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            throw new ArgumentException("Property name cannot be empty.", nameof(propertyName));
        }

        if (parent[propertyName] is null)
        {
            var obj = new JsonObject();
            parent[propertyName] = obj;
            return obj;
        }

        return parent[propertyName] as JsonObject
               ?? throw new InvalidDataException($"MCP config property must be an object: {propertyName}");
    }

    private static string GetAssetTargetPath(
        string agentRoot,
        AgentToolkitInstallOptions options,
        AgentToolkitAsset asset)
    {
        string baseRelativePath = asset.Kind switch
        {
            AgentToolkitAssetKind.Skill => string.IsNullOrWhiteSpace(options.SkillsDirectory)
                ? GetDefaultAssetDirectory(options.Layout, AgentToolkitAssetKind.Skill)
                : options.SkillsDirectory,
            AgentToolkitAssetKind.Subagent => string.IsNullOrWhiteSpace(options.SubagentsDirectory)
                ? GetDefaultAssetDirectory(options.Layout, AgentToolkitAssetKind.Subagent)
                : options.SubagentsDirectory,
            _ => throw new ArgumentOutOfRangeException(nameof(asset)),
        };

        return GetSafeTargetPath(agentRoot, Path.Combine(baseRelativePath, asset.RelativePath));
    }

    private static string GetDefaultAssetDirectory(AgentToolkitInstallLayout layout, AgentToolkitAssetKind kind)
    {
        return (kind, layout) switch
        {
            (AgentToolkitAssetKind.Skill, AgentToolkitInstallLayout.ClaudeCode) => Path.Combine(".claude", "skills"),
            (AgentToolkitAssetKind.Subagent, AgentToolkitInstallLayout.ClaudeCode) => Path.Combine(".claude", "agents"),
            (AgentToolkitAssetKind.Skill, _) => "skills",
            (AgentToolkitAssetKind.Subagent, _) => "agents",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string EnsureDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Agent root cannot be empty.", nameof(path));
        }

        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static string GetSafeTargetPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path cannot be empty.", nameof(relativePath));
        }

        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException($"Path must be relative: {relativePath}", nameof(relativePath));
        }

        if (HasParentTraversal(relativePath))
        {
            throw new ArgumentException($"Path cannot contain parent directory traversal: {relativePath}", nameof(relativePath));
        }

        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        string rootWithSeparator = fullRoot.EndsWith(Path.DirectorySeparatorChar)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.Equals(fullRoot, PathComparison)
            && !fullPath.StartsWith(rootWithSeparator, PathComparison))
        {
            throw new ArgumentException($"Path escapes the agent root: {relativePath}", nameof(relativePath));
        }

        return fullPath;
    }

    private static bool HasParentTraversal(string relativePath)
    {
        return relativePath
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment == "..");
    }

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
