namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal sealed class BrowserDownloadHistoryItem(BrowserDownloadRecord record, bool canImport)
{
    public BrowserDownloadRecord Record { get; } = record;
    public string FileName => Path.GetFileName(Record.FilePath);
    public string FileExtension => Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
    public string SourceHost => new Uri(Record.Url).Host;
    public string DownloadedAt => Record.CompletedAt.ToLocalTime().ToString("g");
    public bool FileExists { get; } = File.Exists(record.FilePath);
    public bool CanImport => canImport && FileExists;
}
