using System.Collections.ObjectModel;
using System.Text.Json;

namespace Beutl.Editor.Components.WebBrowserTab;

internal enum BrowserSearchEngine { Google, Bing }
internal sealed record BrowserBookmark(string Url, string Title);
internal sealed record BrowserDownloadRecord(string Url, string FilePath, DateTimeOffset CompletedAt);

internal sealed class BrowserProfile
{
    private static readonly Lazy<BrowserProfile> s_default = new(() =>
        new BrowserProfile(Path.Combine(BeutlEnvironment.GetHomeDirectoryPath(), "browser", "profile.json")));
    internal static BrowserProfile Default => s_default.Value;
    private readonly string _fileName;
    private bool _corrupt;

    internal BrowserProfile(string fileName)
    {
        _fileName = fileName;
        if (!File.Exists(fileName)) return;
        try
        {
            var data = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(fileName));
            if (data == null || data.Version != 1) throw new JsonException("Unsupported browser profile.");
            Engine = Enum.IsDefined(data.Engine) ? data.Engine : BrowserSearchEngine.Google;
            SuggestionsEnabled = data.SuggestionsEnabled;
            RecordDownloads = data.RecordDownloads;
            foreach (var item in (data.Bookmarks ?? []).Where(x => x != null && IsAllowedUrl(x.Url)).DistinctBy(x => x.Url).Take(200))
                Bookmarks.Add(item);
            foreach (var item in (data.Downloads ?? []).Where(x => x != null && IsAllowedUrl(x.Url)
                         && !string.IsNullOrWhiteSpace(x.FilePath) && Path.IsPathFullyQualified(x.FilePath)).Take(200))
                Downloads.Add(item);
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

    internal bool AddBookmark(Uri uri, string title)
    {
        if (!IsAllowedUrl(uri.AbsoluteUri)) return false;
        var previous = Bookmarks.FirstOrDefault(x => x.Url == uri.AbsoluteUri);
        if (previous != null) Bookmarks.Remove(previous);
        Bookmarks.Insert(0, new BrowserBookmark(uri.AbsoluteUri, string.IsNullOrWhiteSpace(title) ? uri.Host : title));
        if (Bookmarks.Count > 200) Bookmarks.RemoveAt(200);
        return Save();
    }

    internal bool RemoveBookmark(BrowserBookmark item) { Bookmarks.Remove(item); return Save(); }
    internal bool RemoveDownload(BrowserDownloadRecord item) { Downloads.Remove(item); return Save(); }

    internal bool AddDownload(Uri uri, string file)
    {
        if (!RecordDownloads || !IsAllowedUrl(uri.AbsoluteUri)) return true;
        Downloads.Insert(0, new BrowserDownloadRecord(uri.AbsoluteUri, Path.GetFullPath(file), DateTimeOffset.UtcNow));
        if (Downloads.Count > 200) Downloads.RemoveAt(200);
        return Save();
    }

    internal bool UpdateSettings(BrowserSearchEngine engine, bool suggestionsEnabled, bool recordDownloads)
    {
        Engine = engine;
        SuggestionsEnabled = suggestionsEnabled;
        RecordDownloads = recordDownloads;
        bool saved = Save();
        SettingsChanged?.Invoke();
        return saved;
    }

    internal bool ClearHistory()
    {
        Downloads.Clear();
        HistoryCleared?.Invoke();
        return Save();
    }

    private bool Save()
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ProfileData(1, Engine, SuggestionsEnabled,
                RecordDownloads, Bookmarks.ToArray(), Downloads.ToArray())));
            File.Move(temporary, _fileName, overwrite: true);
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
