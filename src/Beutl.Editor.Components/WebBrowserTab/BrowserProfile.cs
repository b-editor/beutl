using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Beutl.Editor.Components.WebBrowserTab;

internal enum BrowserSearchEngine { Google, Bing }
internal sealed record BrowserBookmark(string Url, string Title)
{
    [JsonIgnore]
    public string Host => new Uri(Url).Host;

    [JsonIgnore]
    public string Initial => Host.Length > 4 && Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
        ? Host.Substring(4, 1).ToUpperInvariant()
        : Host[..1].ToUpperInvariant();
}
internal sealed record BrowserDownloadRecord(string Url, string FilePath, DateTimeOffset CompletedAt)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Referrer { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public BrowserReferrerPolicy ReferrerPolicy { get; init; }
}

internal sealed class BrowserProfile
{
    private static readonly Lazy<BrowserProfile> s_default = new(() =>
        new BrowserProfile(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "profile.json")));
    internal static BrowserProfile Default => s_default.Value;
    private readonly string _fileName;
    private bool _corrupt;
    private readonly Action<string>? _writeSnapshot;

    internal BrowserProfile(string fileName, Action<string>? writeSnapshot = null)
    {
        _fileName = fileName;
        _writeSnapshot = writeSnapshot;
        if (!File.Exists(fileName)) return;
        try
        {
            var data = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(fileName));
            if (data == null || data.Version != 1) throw new JsonException("Unsupported browser profile.");
            BrowserBookmark[] bookmarks = (data.Bookmarks ?? []).Where(x => x != null && IsAllowedUrl(x.Url))
                .DistinctBy(x => x.Url).Take(200).ToArray();
            BrowserDownloadRecord[] downloads = (data.Downloads ?? []).Where(x => x != null && IsAllowedUrl(x.Url)
                && !string.IsNullOrWhiteSpace(x.FilePath) && Path.IsPathFullyQualified(x.FilePath)).Take(200)
                .Select(item =>
                {
                    Uri.TryCreate(item.Referrer, UriKind.Absolute, out Uri? referrer);
                    return item with { Referrer = BrowserMediaDownload.NormalizeReferrer(referrer, new Uri(item.Url))?.AbsoluteUri };
                }).ToArray();
            BrowserSearchEngine engine = Enum.IsDefined(data.Engine) ? data.Engine : BrowserSearchEngine.Google;
            if (engine != data.Engine || !bookmarks.SequenceEqual(data.Bookmarks ?? []) || !downloads.SequenceEqual(data.Downloads ?? []))
                Save(data with { Engine = engine, Bookmarks = bookmarks, Downloads = downloads });
            Engine = engine;
            SuggestionsEnabled = data.SuggestionsEnabled;
            RecordDownloads = data.RecordDownloads;
            foreach (BrowserBookmark item in bookmarks) Bookmarks.Add(item);
            foreach (BrowserDownloadRecord item in downloads) Downloads.Add(item);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Error = ex.Message;
            _corrupt = true;
        }
    }

    internal ObservableCollection<BrowserBookmark> Bookmarks { get; } = [];
    internal ObservableCollection<BrowserDownloadRecord> Downloads { get; } = [];
    internal BrowserSearchEngine Engine { get; private set; }
    internal bool SuggestionsEnabled { get; private set; } = true;
    internal bool RecordDownloads { get; private set; } = true;
    internal string? Error { get; private set; }
    internal event Action? SettingsChanged;
    internal event Action? HistoryCleared;

    internal static bool IsAllowedUrl(string? value) => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && BrowserMediaDownload.IsHttpUri(uri);

    private ProfileData Snapshot() => new(1, Engine, SuggestionsEnabled, RecordDownloads, Bookmarks.ToArray(), Downloads.ToArray());

    internal bool AddBookmark(Uri uri, string title)
    {
        if (!IsAllowedUrl(uri.AbsoluteUri)) return false;
        var item = new BrowserBookmark(uri.AbsoluteUri, string.IsNullOrWhiteSpace(title) ? uri.Host : title);
        BrowserBookmark[] previous = Bookmarks.Where(x => x.Url == uri.AbsoluteUri).ToArray();
        BrowserBookmark[] candidate = Bookmarks.Except(previous).Prepend(item).Take(200).ToArray();
        if (!Save(Snapshot() with { Bookmarks = candidate })) return false;
        foreach (BrowserBookmark duplicate in previous) Bookmarks.Remove(duplicate);
        Bookmarks.Insert(0, item);
        if (Bookmarks.Count > 200) Bookmarks.RemoveAt(200);
        return true;
    }

    internal bool RemoveBookmark(BrowserBookmark item)
    {
        var candidate = Bookmarks.ToList();
        candidate.Remove(item);
        if (!Save(Snapshot() with { Bookmarks = candidate.ToArray() })) return false;
        Bookmarks.Remove(item);
        return true;
    }

    internal bool RemoveDownload(BrowserDownloadRecord item)
    {
        var candidate = Downloads.ToList();
        candidate.Remove(item);
        if (!Save(Snapshot() with { Downloads = candidate.ToArray() })) return false;
        Downloads.Remove(item);
        return true;
    }

    internal bool AddDownload(Uri uri, string file, Uri? referrer = null, BrowserReferrerPolicy referrerPolicy = BrowserReferrerPolicy.Origin)
    {
        if (!RecordDownloads || !IsAllowedUrl(uri.AbsoluteUri)) return true;
        var item = new BrowserDownloadRecord(uri.AbsoluteUri, Path.GetFullPath(file), DateTimeOffset.UtcNow)
        {
            Referrer = BrowserMediaDownload.NormalizeReferrer(referrer, uri)?.AbsoluteUri,
            ReferrerPolicy = referrerPolicy
        };
        if (!Save(Snapshot() with { Downloads = Downloads.Prepend(item).Take(200).ToArray() })) return false;
        Downloads.Insert(0, item);
        if (Downloads.Count > 200) Downloads.RemoveAt(200);
        return true;
    }

    internal bool UpdateSettings(BrowserSearchEngine engine, bool suggestionsEnabled, bool recordDownloads)
    {
        if (!Save(Snapshot() with { Engine = engine, SuggestionsEnabled = suggestionsEnabled, RecordDownloads = recordDownloads })) return false;
        Engine = engine;
        SuggestionsEnabled = suggestionsEnabled;
        RecordDownloads = recordDownloads;
        SettingsChanged?.Invoke();
        return true;
    }

    internal bool ClearHistory()
    {
        if (!Save(Snapshot() with { Downloads = [] })) return false;
        Downloads.Clear();
        HistoryCleared?.Invoke();
        return true;
    }

    private bool Save(ProfileData snapshot)
    {
        string temporary = _fileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_fileName)!);
            if (_corrupt && File.Exists(_fileName))
            {
                File.Copy(_fileName, _fileName + ".recovery-" + Guid.NewGuid().ToString("N"));
                _corrupt = false;
            }
            string json = JsonSerializer.Serialize(snapshot);
            if (_writeSnapshot != null) _writeSnapshot(json);
            else
            {
                File.WriteAllText(temporary, json);
                File.Move(temporary, _fileName, overwrite: true);
            }
            _corrupt = false;
            Error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = ex.Message;
            return false;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error ??= ex.Message; }
        }
    }

    private sealed record ProfileData(int Version, BrowserSearchEngine Engine, bool SuggestionsEnabled,
        bool RecordDownloads, BrowserBookmark[]? Bookmarks, BrowserDownloadRecord[]? Downloads);
}
