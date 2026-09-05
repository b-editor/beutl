using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// Everything a <see cref="SpectrumShape"/> needs to paint one frame of a spectrum.
/// </summary>
/// <remarks>
/// <para>
/// This is a <c>ref struct</c>, so it cannot be stored in a field, captured by a lambda, or boxed. That
/// restriction is deliberate: <see cref="NormalizedBars"/> points at a buffer the engine reuses between
/// frames, and it is valid only for the duration of the
/// <see cref="SpectrumShape.Resource.Render(in SpectrumRenderContext)"/> call that received the context.
/// Copy out whatever has to outlive that call.
/// </para>
/// <para>
/// Passing the arguments as a context rather than as a positional parameter list lets the engine hand a
/// shape new information later on without breaking shapes compiled against an earlier release.
/// </para>
/// </remarks>
public readonly ref struct SpectrumRenderContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpectrumRenderContext"/> struct.
    /// </summary>
    /// <param name="canvas">The canvas to paint on.</param>
    /// <param name="bounds">The rectangle, in the canvas' coordinate space, the spectrum occupies.</param>
    /// <param name="normalizedBars">The per-bar intensities, already normalized to 0..1.</param>
    /// <param name="fill">The brush the visualizer is configured to paint with.</param>
    public SpectrumRenderContext(
        ImmediateCanvas canvas,
        Rect bounds,
        ReadOnlySpan<float> normalizedBars,
        Brush.Resource fill)
    {
        Canvas = canvas;
        Bounds = bounds;
        NormalizedBars = normalizedBars;
        Fill = fill;
    }

    /// <summary>
    /// Gets the canvas to paint on.
    /// </summary>
    public ImmediateCanvas Canvas { get; }

    /// <summary>
    /// Gets the rectangle, in the canvas' coordinate space, the spectrum occupies.
    /// </summary>
    public Rect Bounds { get; }

    /// <summary>
    /// Gets the per-bar intensities, one entry per bar ordered from the lowest frequency band upwards.
    /// </summary>
    /// <remarks>
    /// The values are already normalized to 0..1, so a shape maps them straight onto its geometry; there is
    /// no gain or decibel conversion left to apply. The span is valid only for the duration of the
    /// <see cref="SpectrumShape.Resource.Render(in SpectrumRenderContext)"/> call.
    /// </remarks>
    public ReadOnlySpan<float> NormalizedBars { get; }

    /// <summary>
    /// Gets the brush the visualizer is configured to paint with.
    /// </summary>
    public Brush.Resource Fill { get; }
}
