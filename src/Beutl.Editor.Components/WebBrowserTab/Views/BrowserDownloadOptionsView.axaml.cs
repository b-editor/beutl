using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class BrowserDownloadOptionsView : UserControl
{
    public BrowserDownloadOptionsView()
    {
        InitializeComponent();
    }

    public BrowserDownloadOptionsView(Uri uri, string? projectDirectory, string materialsDirectory, bool canImport)
        : this()
    {
        SourceUrl = uri.AbsoluteUri;
        SourceHost = uri.Host;
        FileName = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(FileName)) FileName = uri.Host;
        ProjectDirectory = projectDirectory;
        MaterialsDirectory = materialsDirectory;
        CanImport = canImport;
        DataContext = this;
        ProjectDestination.IsChecked = HasProject;
        MaterialsDestination.IsChecked = !HasProject;
        AddToTimelineCheckBox.IsChecked = canImport;
    }

    public string SourceUrl { get; } = string.Empty;
    public string SourceHost { get; } = string.Empty;
    public string FileName { get; } = string.Empty;
    public string? ProjectDirectory { get; }
    public string ProjectLocation => ProjectDirectory ?? "—";
    public string MaterialsDirectory { get; } = string.Empty;
    public bool HasProject => ProjectDirectory != null;
    public bool CanImport { get; }
    public string DestinationGroup { get; } = Guid.NewGuid().ToString("N");
    internal string SelectedDirectory => HasProject && ProjectDestination.IsChecked == true ? ProjectDirectory! : MaterialsDirectory;
    internal bool AddToTimeline => CanImport && AddToTimelineCheckBox.IsChecked == true;
    internal event Action? Confirmed;
    internal event Action? Canceled;

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Confirmed?.Invoke();
    private void OnCancelClick(object? sender, RoutedEventArgs e) => Canceled?.Invoke();
}
