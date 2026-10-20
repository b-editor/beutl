using Beutl.AgentToolkit.Common;

namespace Beutl.AgentToolkit.Installation;

public static class AgentToolkitInstaller
{
    public static async Task<AgentToolkitInstallResult> InstallAsync(
        AgentToolkitInstallOptions options,
        IEnumerable<AgentToolkitAsset> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(assets);

        string agentRoot = EnsureDirectory(options.AgentRoot);
        var installedFiles = new List<string>();
        var assetFileRecords = new List<InstalledFileRecord>();
        var backedUpFiles = new List<BackedUpFile>();

        foreach (AgentToolkitAsset asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((asset.Kind == AgentToolkitAssetKind.Skill && !options.InstallSkills)
                || (asset.Kind == AgentToolkitAssetKind.Subagent && !options.InstallSubagents))
            {
                continue;
            }

            string relativePath = asset.RelativePath;
            string content = asset.Content;
            if (asset.Kind == AgentToolkitAssetKind.Subagent
                && options.SubagentFormat == SubagentFileFormat.CodexToml)
            {
                relativePath = Path.ChangeExtension(relativePath, ".toml");
                content = CodexSubagentConverter.Convert(
                    asset.Content,
                    Path.GetFileNameWithoutExtension(asset.RelativePath));
            }

            string targetPath = GetAssetTargetPath(agentRoot, options, asset.Kind, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            if (await BackUpEditedFileAsync(targetPath, content, options.PreviousFiles, cancellationToken)
                    .ConfigureAwait(false) is { } backup)
            {
                backedUpFiles.Add(backup);
            }

            await File.WriteAllTextAsync(targetPath, content, cancellationToken).ConfigureAwait(false);
            installedFiles.Add(targetPath);
            assetFileRecords.Add(new InstalledFileRecord(
                targetPath, AgentToolkitInstallManifestStore.ComputeContentHash(content)));
        }

        string? mcpConfigPath = null;
        ManualMcpEdit? manualMcpEdit = null;
        if (options.InstallMcp)
        {
            mcpConfigPath = GetSafeTargetPath(options.McpConfigRoot ?? agentRoot, options.McpConfigFileName);
            if (options.McpConfigFormat == McpConfigFormat.CodexToml)
            {
                await CodexMcpConfigWriter.WriteAsync(mcpConfigPath, options, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                manualMcpEdit = await JsonMcpConfigWriter.WriteAsync(mcpConfigPath, options, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (manualMcpEdit is null)
            {
                installedFiles.Add(mcpConfigPath);
            }
        }

        return new AgentToolkitInstallResult(
            installedFiles,
            options.InstallMcp && manualMcpEdit is null,
            assetFileRecords)
        {
            ManualMcpEdit = manualMcpEdit,
            BackedUpFiles = backedUpFiles,
        };
    }

    // The entry of earlier layouts, removed from the agent configuration on reinstall.
    internal static IEnumerable<string> ObsoleteServerNames() => [AgentToolkitInstallOptions.LegacyLiveServerName];

    // A file the user edited since an earlier install wrote it, or one no install recorded, is copied
    // aside before the bundled version replaces it. An unedited copy of an earlier install is not.
    private static async Task<BackedUpFile?> BackUpEditedFileAsync(
        string path,
        string content,
        IReadOnlyList<InstalledFileRecord> previousFiles,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string existing = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (existing == content)
        {
            return null;
        }

        string hash = AgentToolkitInstallManifestStore.ComputeContentHash(existing);
        if (previousFiles.Any(record => AgentToolkitInstallManifestStore.PathComparer.Equals(record.Path, path)
                                        && string.Equals(record.Sha256, hash, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        string stem = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}";
        string backupPath = $"{stem}.bak";
        for (int index = 2; File.Exists(backupPath); index++)
        {
            backupPath = $"{stem}-{index}.bak";
        }

        File.Copy(path, backupPath);
        return new BackedUpFile(path, backupPath);
    }

    private static string GetAssetTargetPath(
        string agentRoot,
        AgentToolkitInstallOptions options,
        AgentToolkitAssetKind kind,
        string relativePath)
    {
        string baseRelativePath = kind switch
        {
            AgentToolkitAssetKind.Skill => options.SkillsDirectory,
            AgentToolkitAssetKind.Subagent => options.SubagentsDirectory,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        return GetSafeTargetPath(agentRoot, Path.Combine(baseRelativePath, relativePath));
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

        string fullRoot = FilePathComparison.ResolveCanonicalPath(Path.GetFullPath(root));
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));

        // Resolve through symlinks: a purely textual check would let an existing symlink/junction
        // under the root redirect the write outside it.
        if (!FilePathComparison.IsSameOrDescendant(fullRoot, fullPath))
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
}
