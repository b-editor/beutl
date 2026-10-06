using System.Text.Json;
using Beutl.Media.Proxy;

namespace Beutl.Extensions.FFmpeg.Proxy;

public sealed partial class FFmpegProxyGenerator
{
    private static string GetMetadataPath(string finalPath)
        => Path.Combine(Path.GetDirectoryName(finalPath)!, "meta.json");

    internal static void WriteMetadata(string finalPath, ProxyEntry entry)
    {
        string metadataPath = GetMetadataPath(finalPath);
        ProxyEntry[] entries = ReadMetadataEntries(metadataPath, entry.Source)
            .Where(existing => existing.Preset != entry.Preset)
            .Append(entry)
            .ToArray();
        WriteMetadataFile(metadataPath, entry.Source, entries);
    }

    private static void RemoveMetadataEntry(string finalPath, ProxyEntry entry)
    {
        try
        {
            string metadataPath = GetMetadataPath(finalPath);
            if (!File.Exists(metadataPath))
                return;

            ProxyEntry[] entries = ReadMetadataEntries(metadataPath, entry.Source)
                .Where(existing => existing.Preset != entry.Preset || existing.ProxyFileRelative != entry.ProxyFileRelative)
                .ToArray();
            if (entries.Length == 0)
            {
                File.Delete(metadataPath);
                return;
            }

            WriteMetadataFile(metadataPath, entry.Source, entries);
        }
        catch
        {
        }
    }

    private static void WriteMetadataFile(string metadataPath, ProxyFingerprint source, ProxyEntry[] entries)
    {
        var metadata = new ProxySourceMetadata
        {
            Source = source,
            Entries = [.. entries],
        };
        File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, s_jsonOptions));
    }

    private static IEnumerable<ProxyEntry> ReadMetadataEntries(string metadataPath, ProxyFingerprint source)
    {
        if (!File.Exists(metadataPath))
            yield break;

        ProxySourceMetadata? metadata = null;
        try
        {
            metadata = JsonSerializer.Deserialize<ProxySourceMetadata>(
                File.ReadAllText(metadataPath),
                s_jsonOptions);
        }
        catch
        {
        }

        if (metadata is not { Version: ProxySourceMetadata.CurrentVersion }
            || metadata.Source != source)
        {
            yield break;
        }

        foreach (ProxyEntry entry in metadata.Entries)
        {
            if (entry.Source == source)
                yield return entry;
        }
    }
}
