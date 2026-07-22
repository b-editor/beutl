using System.Reactive.Disposables;

using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Models;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the save-frame scale-choice dialog.
/// </summary>
public sealed class SaveFrameDialogViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public SaveFrameDialogViewModel(PixelSize baseSize)
    {
        BaseSize = baseSize;

        OutputSizeText = SelectedScale
            .Select(scale =>
            {
                (long width, long height) = SaveFrameScale.GetRenderSize(baseSize, scale);
                return $"{width} × {height} px";
            })
            .ToReadOnlyReactivePropertySlim("")
            .DisposeWith(_disposables);

        Warning = SelectedScale
            .Select(scale =>
            {
                if (SaveFrameScale.FitsBufferLimit(baseSize, scale)) return null;

                (long width, long height) = SaveFrameScale.GetRenderSize(baseSize, scale);
                return string.Format(
                    MessageStrings.SaveImageExceedsMaxRenderSize,
                    scale, width, height, RenderScaleUtilities.MaxBufferDimension);
            })
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CanSave = Warning
            .Select(w => w == null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
    }

    /// <summary>The logical size the multiplier is applied to (the scene frame size).</summary>
    public PixelSize BaseSize { get; }

    /// <summary>The selectable output-resolution multipliers.</summary>
    public IReadOnlyList<float> ScaleOptions { get; } = SaveFrameScale.Factors;

    /// <summary>The chosen output-resolution multiplier; defaults to <c>1×</c> (full resolution).</summary>
    public ReactiveProperty<float> SelectedScale { get; } = new(1f);

    /// <summary>The resulting image size at the current scale, e.g. <c>3840 × 2160 px</c>.</summary>
    public ReadOnlyReactivePropertySlim<string> OutputSizeText { get; }

    /// <summary>Localized warning when the scaled surface exceeds the per-axis buffer limit; otherwise null.</summary>
    public ReadOnlyReactivePropertySlim<string?> Warning { get; }

    /// <summary>Whether the current scale fits the buffer limit and can be rendered.</summary>
    public ReadOnlyReactivePropertySlim<bool> CanSave { get; }

    public void Dispose()
    {
        SelectedScale.Dispose();
        _disposables.Dispose();
    }
}
