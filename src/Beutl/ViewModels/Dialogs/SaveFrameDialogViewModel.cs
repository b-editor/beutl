using Beutl.Graphics.Rendering;
using Beutl.Helpers;
using Beutl.Language;
using Beutl.Media;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// Backs the save-frame scale-choice dialog: the user picks an output-resolution multiplier before the
/// current frame / selected element is rendered and saved. The chosen <see cref="SelectedScale"/> drives
/// a one-shot full-fidelity render at <c>ceil(<paramref name="baseSize"/> × scale)</c>.
/// </summary>
public sealed class SaveFrameDialogViewModel
{
    public SaveFrameDialogViewModel(PixelSize baseSize)
    {
        BaseSize = baseSize;

        OutputSizeText = SelectedScale
            .Select(scale =>
            {
                (long width, long height) = SaveFrameScale.GetRenderSize(baseSize, scale);
                return $"{width} × {height} px";
            })
            .ToReadOnlyReactivePropertySlim("");

        Warning = SelectedScale
            .Select(scale =>
            {
                if (SaveFrameScale.FitsBufferLimit(baseSize, scale)) return null;

                (long width, long height) = SaveFrameScale.GetRenderSize(baseSize, scale);
                return string.Format(
                    MessageStrings.SaveImageExceedsMaxRenderSize,
                    scale, width, height, RenderNodeContext.MaxBufferDimension);
            })
            .ToReadOnlyReactivePropertySlim();

        CanSave = Warning
            .Select(w => w == null)
            .ToReadOnlyReactivePropertySlim();
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
}
