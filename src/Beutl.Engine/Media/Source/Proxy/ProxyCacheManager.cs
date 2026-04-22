using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Media.Source.Proxy;

public sealed class ProxyCacheManager : IProxyCacheManager
{
    private const int SchemaVersion = 1;
    private const string MetaExtension = ".meta.json";

    private static readonly ILogger s_logger = Log.CreateLogger<ProxyCacheManager>();
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Lazy<ProxyCacheManager> s_instance = new(() => new ProxyCacheManager());

    private readonly ConcurrentDictionary<string, ProxyEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProxyStatus> _transient = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scanLock = new();
    private bool _scanned;

    public static IProxyCacheManager Instance => s_instance.Value;

    public string CacheDirectory => GlobalConfiguration.Instance.ProxyConfig.CacheDirectory;

    public bool TryGetProxyPath(string originalPath, [NotNullWhen(true)] out string? proxyPath)
    {
        proxyPath = null;
        if (!GlobalConfiguration.Instance.ProxyConfig.IsEnabled) return false;
        if (string.IsNullOrEmpty(originalPath)) return false;

        EnsureScanned();

        string normalized = Normalize(originalPath);
        if (!_entries.TryGetValue(normalized, out var entry)) return false;
        if (IsStale(entry)) return false;
        if (!File.Exists(entry.ProxyPath)) return false;

        proxyPath = entry.ProxyPath;
        return true;
    }

    public ProxyStatus GetStatus(string originalPath)
    {
        if (string.IsNullOrEmpty(originalPath)) return ProxyStatus.NotGenerated;

        EnsureScanned();

        string normalized = Normalize(originalPath);
        if (_transient.TryGetValue(normalized, out var transient)) return transient;
        if (!_entries.TryGetValue(normalized, out var entry)) return ProxyStatus.NotGenerated;
        if (IsStale(entry)) return ProxyStatus.Stale;
        if (!File.Exists(entry.ProxyPath)) return ProxyStatus.NotGenerated;

        return ProxyStatus.Available;
    }

    public string ComputeKey(string originalPath)
    {
        var info = new FileInfo(originalPath);
        info.Refresh();
        long size = info.Exists ? info.Length : 0;
        long ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
        string seed = $"{Normalize(originalPath)}|{size}|{ticks}";
        byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }

    public ProxyEntry? TryGetEntry(string originalPath)
    {
        EnsureScanned();
        _entries.TryGetValue(Normalize(originalPath), out var entry);
        if (entry != null && IsStale(entry)) return null;
        return entry;
    }

    public IEnumerable<ProxyEntry> Enumerate()
    {
        EnsureScanned();
        return _entries.Values.ToArray();
    }

    public long GetTotalSizeBytes()
    {
        EnsureScanned();
        long total = 0;
        foreach (var entry in _entries.Values)
        {
            total += entry.ProxyFileSize;
        }
        return total;
    }

    public void Invalidate(string originalPath)
    {
        _entries.TryRemove(Normalize(originalPath), out _);
    }

    public void Delete(string originalPath)
    {
        string normalized = Normalize(originalPath);
        if (_entries.TryRemove(normalized, out var entry))
        {
            SafeDeleteProxyFiles(entry);
        }
    }

    public void TrimCache(long maxBytes)
    {
        EnsureScanned();
        long total = GetTotalSizeBytes();
        if (total <= maxBytes) return;

        var ordered = _entries.Values.OrderBy(e => e.GeneratedAt).ToList();
        foreach (var entry in ordered)
        {
            if (total <= maxBytes) break;
            if (_entries.TryRemove(Normalize(entry.OriginalPath), out var removed))
            {
                total -= removed.ProxyFileSize;
                SafeDeleteProxyFiles(removed);
            }
        }
    }

    public void Register(ProxyEntry entry)
    {
        string dir = GetCacheDirectoryOrCreate();
        string metaPath = Path.Combine(dir, Path.GetFileName(entry.ProxyPath) + MetaExtension);

        try
        {
            using var stream = File.Create(metaPath);
            JsonSerializer.Serialize(stream, entry, s_jsonOptions);
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to write proxy meta file: {Path}", metaPath);
        }

        string normalized = Normalize(entry.OriginalPath);
        _entries[normalized] = entry;
        _transient.TryRemove(normalized, out _);
    }

    public void MarkGenerating(string originalPath)
    {
        _transient[Normalize(originalPath)] = ProxyStatus.Generating;
    }

    public void MarkFailed(string originalPath)
    {
        _transient[Normalize(originalPath)] = ProxyStatus.Failed;
    }

    private void EnsureScanned()
    {
        if (_scanned) return;
        lock (_scanLock)
        {
            if (_scanned) return;
            ScanCacheDirectory();
            _scanned = true;
        }
    }

    private void ScanCacheDirectory()
    {
        string dir = CacheDirectory;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

        foreach (string metaPath in Directory.EnumerateFiles(dir, "*" + MetaExtension, SearchOption.TopDirectoryOnly))
        {
            try
            {
                using var stream = File.OpenRead(metaPath);
                var entry = JsonSerializer.Deserialize<ProxyEntry>(stream, s_jsonOptions);
                if (entry == null || entry.SchemaVersion != SchemaVersion) continue;
                if (!File.Exists(entry.ProxyPath)) continue;

                _entries[Normalize(entry.OriginalPath)] = entry;
            }
            catch (Exception ex)
            {
                s_logger.LogWarning(ex, "Failed to load proxy meta: {Path}", metaPath);
            }
        }
    }

    private static bool IsStale(ProxyEntry entry)
    {
        try
        {
            var info = new FileInfo(entry.OriginalPath);
            info.Refresh();
            if (!info.Exists) return true;
            if (info.Length != entry.OriginalSize) return true;
            if (info.LastWriteTimeUtc != entry.OriginalMtime) return true;
            return false;
        }
        catch
        {
            return true;
        }
    }

    private void SafeDeleteProxyFiles(ProxyEntry entry)
    {
        TryDelete(entry.ProxyPath);
        TryDelete(entry.ProxyPath + MetaExtension);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug(ex, "Failed to delete proxy file: {Path}", path);
        }
    }

    private string GetCacheDirectoryOrCreate()
    {
        string dir = CacheDirectory;
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
