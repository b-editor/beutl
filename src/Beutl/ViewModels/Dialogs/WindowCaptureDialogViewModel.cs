using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class WindowCaptureDialogViewModel
{
    public WindowCaptureDialogViewModel()
    {
        CanStart = OutputPath
            .CombineLatest(Scale, FrameRate, (path, scale, fps) =>
                !string.IsNullOrWhiteSpace(path)
                && path!.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                && scale > 0
                && fps > 0)
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactiveProperty<double> Scale { get; } = new(1.0);

    public ReactiveProperty<int> FrameRate { get; } = new(30);

    public ReactiveProperty<string?> OutputPath { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanStart { get; }
}
