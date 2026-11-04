using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.AgentToolkit.Installation;

public sealed record InstalledFileRecord(string Path, string Sha256);

/// <summary>
/// One place an install put the Beutl MCP entry, with the layout it wrote there: a configuration
/// file (<see cref="Location"/> is its path) or an agent's own registry (the agent's name and scope).
/// </summary>
public sealed record InstalledMcpTarget(string Location, bool IsConfigFile, int Layout);

/// <summary>
/// Snapshot of the last settings-page install: the combined hash of the
/// bundled assets that were installed, every asset file written and its
/// content hash, the layout of the MCP entries, and the targets known to hold one.
/// Enables update detection after an app update and safe cleanup of files a newer
/// bundle no longer ships. <paramref name="McpLayout"/> advances only once every
/// target in <paramref name="McpTargets"/> is current; manifests from before targets
/// were recorded have none, so their layout can only be tracked as a whole.
/// </summary>
public sealed record AgentToolkitInstallManifest(
    string AssetsHash,
    IReadOnlyList<InstalledFileRecord> Files,
    int McpLayout = 0,
    IReadOnlyList<InstalledMcpTarget>? McpTargets = null)
{
    /// <summary>
    /// The MCP entries the current installer writes. Manifests from earlier versions carry no
    /// layout (0): those installs wrote a stdio "beutl-agent" entry next to a "beutl-live" URL
    /// entry holding the live bearer token. Layout 1 is the single "beutl-agent" stdio server that
    /// also edits running editors, so an older manifest asks the user to reinstall.
    /// </summary>
    public const int CurrentMcpLayout = 1;
}

public static class AgentToolkitInstallManifestStore
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
    };

    public static string GetDefaultPath()
    {
        return Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "agent-toolkit-install.json");
    }

    public static AgentToolkitInstallManifest? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<AgentToolkitInstallManifest>(
                File.ReadAllText(path), s_jsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string path, AgentToolkitInstallManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, s_jsonOptions));
    }

    public static bool IsUpdateAvailable(
        AgentToolkitInstallManifest? manifest,
        IEnumerable<AgentToolkitAsset> bundledAssets)
    {
        return manifest is not null
               && (!string.Equals(manifest.AssetsHash, ComputeAssetsHash(bundledAssets), StringComparison.Ordinal)
                   || IsMcpLayoutOutdated(manifest));
    }

    /// <summary>
    /// True when the recorded install wrote MCP entries of an earlier layout, so the agent
    /// configuration still needs the reinstall that replaces them.
    /// </summary>
    public static bool IsMcpLayoutOutdated(AgentToolkitInstallManifest? manifest)
    {
        return manifest is not null && manifest.McpLayout < AgentToolkitInstallManifest.CurrentMcpLayout;
    }

    /// <summary>
    /// The recorded targets an install of the current layout has not reached. A configuration
    /// file that is gone has no entry left to replace.
    /// </summary>
    public static IReadOnlyList<InstalledMcpTarget> GetOutdatedMcpTargets(AgentToolkitInstallManifest? manifest)
    {
        return manifest?.McpTargets is { } targets
            ? [.. targets.Where(target => target.Layout < AgentToolkitInstallManifest.CurrentMcpLayout && StillExists(target))]
            : [];
    }

    /// <summary>
    /// The targets to record after an install: the earlier ones with <paramref name="installed"/> in
    /// place of its old record, without configuration files that are gone.
    /// </summary>
    public static IReadOnlyList<InstalledMcpTarget> UpdateMcpTargets(
        IReadOnlyList<InstalledMcpTarget>? previous,
        InstalledMcpTarget? installed)
    {
        InstalledMcpTarget[] kept = previous is null
            ? []
            : [.. previous.Where(target => StillExists(target)
                                           && (installed is null || !PathComparer.Equals(target.Location, installed.Location)))];
        return installed is null ? kept : [installed, .. kept];
    }

    private static bool StillExists(InstalledMcpTarget target)
    {
        return !target.IsConfigFile || File.Exists(target.Location);
    }

    public static string ComputeAssetsHash(IEnumerable<AgentToolkitAsset> assets)
    {
        var builder = new StringBuilder();
        foreach (AgentToolkitAsset asset in assets
                     .OrderBy(a => a.Kind)
                     .ThenBy(a => a.RelativePath, StringComparer.Ordinal))
        {
            builder.Append(asset.Kind)
                .Append('|')
                .Append(asset.RelativePath)
                .Append('|')
                .Append(ComputeContentHash(asset.Content))
                .Append('\n');
        }

        return ComputeContentHash(builder.ToString());
    }

    public static string ComputeContentHash(string content)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    /// <summary>
    /// Deletes files from a previous install that the current install no
    /// longer writes (renamed/removed skills). A file whose on-disk content
    /// no longer matches the recorded hash was edited by the user and is
    /// left in place. Returns the deleted paths.
    /// </summary>
    public static IReadOnlyList<string> RemoveStaleFiles(
        IEnumerable<InstalledFileRecord> previousFiles,
        IReadOnlySet<string> currentPaths)
    {
        var deleted = new List<string>();
        foreach (InstalledFileRecord record in previousFiles)
        {
            if (currentPaths.Contains(record.Path) || !File.Exists(record.Path))
            {
                continue;
            }

            string onDiskHash;
            try
            {
                onDiskHash = ComputeContentHash(File.ReadAllText(record.Path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!string.Equals(onDiskHash, record.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                File.Delete(record.Path);
                deleted.Add(record.Path);
                DeleteDirectoryIfEmpty(Path.GetDirectoryName(record.Path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    public static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static void DeleteDirectoryIfEmpty(string? directory)
    {
        try
        {
            if (directory is not null
                && Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
