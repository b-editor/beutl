using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Beutl.AgentToolkit.Installation;

public sealed record InstalledFileRecord(string Path, string Sha256);

/// <summary>
/// Snapshot of the last settings-page install: the combined hash of the
/// bundled assets that were installed, plus every asset file written and its
/// content hash. Enables update detection after an app update and safe
/// cleanup of files a newer bundle no longer ships.
/// </summary>
public sealed record AgentToolkitInstallManifest(
    string AssetsHash,
    IReadOnlyList<InstalledFileRecord> Files);

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
               && !string.Equals(manifest.AssetsHash, ComputeAssetsHash(bundledAssets), StringComparison.Ordinal);
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
