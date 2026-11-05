namespace Beutl.Views;

// A choice in the preview's zoom box. A null level fits the frame to the panel.
internal sealed class PreviewZoomOption(string label, double? level)
{
    public string Label { get; } = label;

    public double? Level { get; } = level;

    public string? Shortcut => Level is null
        ? PlayerView.GetPreviewCommandGesture("editor.previewZoomFit")?.ToString("p", null)
        : null;
}
