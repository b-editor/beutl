using System.Text.Json.Nodes;

namespace Beutl.Editor;

// The hashes this machine took of a scene's media, by absolute path, length and last-write time, kept out of the
// shared files in <scene folder>/.beutl/media-hashes.json. A missing or unreadable file reads as empty.
internal sealed class MediaHashCache
{
    internal const string FileName = "media-hashes.json";

    private const string LengthKey = "Length";
    private const string TimeKey = "LastWriteTimeUtcTicks";
    private const string HashKey = "Sha256";

    private readonly string _path;
    private readonly Dictionary<string, Entry> _stored;
    private readonly Dictionary<string, Entry> _read = new(StringComparer.Ordinal);

    private MediaHashCache(string path, Dictionary<string, Entry> stored)
    {
        _path = path;
        _stored = stored;
    }

    public static MediaHashCache Load(Uri sceneUri)
    {
        string path = Path.Combine(Path.GetDirectoryName(sceneUri.LocalPath)!, EditorConstants.BeutlFolder, FileName);
        var stored = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            if (JsonHelper.JsonRestore(path) is JsonObject json)
            {
                foreach ((string file, JsonNode? node) in json)
                {
                    if (node is JsonObject entry
                        && entry[LengthKey] is JsonValue length && length.TryGetValue(out long lengthValue)
                        && entry[TimeKey] is JsonValue time && time.TryGetValue(out long timeValue)
                        && entry[HashKey] is JsonValue hash && hash.TryGetValue(out string? hashValue)
                        && !string.IsNullOrEmpty(hashValue))
                    {
                        stored[file] = new Entry(lengthValue, timeValue, hashValue);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return new MediaHashCache(path, stored);
    }

    public string? Find(FileInfo file)
        => _stored.TryGetValue(file.FullName, out Entry? entry)
           && entry.Length == file.Length
           && entry.LastWriteTimeUtcTicks == file.LastWriteTimeUtc.Ticks
            ? entry.Sha256
            : null;

    public void Add(FileInfo file, string hash)
        => _read[file.FullName] = new Entry(file.Length, file.LastWriteTimeUtc.Ticks, hash);

    // A pass that read every file of the scene keeps only those; another one adds to what was stored.
    public void Save(bool readEveryFile)
    {
        var entries = new SortedDictionary<string, Entry>(StringComparer.Ordinal);
        foreach ((string file, Entry entry) in readEveryFile ? _read : _stored.Concat(_read))
        {
            entries[file] = entry;
        }

        if (entries.Count == _stored.Count
            && entries.All(pair => _stored.TryGetValue(pair.Key, out Entry? stored) && stored == pair.Value))
            return;

        var json = new JsonObject();
        foreach ((string file, Entry entry) in entries)
        {
            json[file] = new JsonObject
            {
                [LengthKey] = entry.Length,
                [TimeKey] = entry.LastWriteTimeUtcTicks,
                [HashKey] = entry.Sha256,
            };
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        json.JsonSave(_path);
    }

    private sealed record Entry(long Length, long LastWriteTimeUtcTicks, string Sha256);
}
