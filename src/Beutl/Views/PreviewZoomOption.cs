using System.ComponentModel;

namespace Beutl.Views;

// A choice in the preview's zoom box. A null level fits the frame to the panel.
internal sealed class PreviewZoomOption(string label, double? level) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Label { get; } = label;

    public double? Level { get; } = level;

    public string? Shortcut => Level is null
        ? PlayerView.GetPreviewCommandGesture("editor.previewZoomFit")?.ToString("p", null)
        : null;

    public void RefreshShortcut() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shortcut)));
}
