using Beutl.Media;

namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// Everything a <see cref="WaveformShape"/> needs to paint one frame of a waveform.
/// </summary>
/// <remarks>
/// <para>
/// This is a <c>ref struct</c>, so it cannot be stored in a field, captured by a lambda, or boxed. That
/// restriction is deliberate: <see cref="Mins"/> and <see cref="Maxs"/> point at buffers the engine reuses
/// between frames, and they are valid only for the duration of the
/// <see cref="WaveformShape.Resource.Render(in WaveformRenderContext)"/> call that received the context.
/// Copy out whatever has to outlive that call.
/// </para>
/// <para>
/// Passing the arguments as a context rather than as a positional parameter list lets the engine hand a
/// shape new information later on without breaking shapes compiled against an earlier release.
/// </para>
/// </remarks>
public readonly ref struct WaveformRenderContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WaveformRenderContext"/> struct.
    /// </summary>
    /// <param name="canvas">The canvas to paint on.</param>
    /// <param name="bounds">The rectangle, in the canvas' coordinate space, the waveform occupies.</param>
    /// <param name="mins">The per-slot minimum sample values, with no gain applied.</param>
    /// <param name="maxs">The per-slot maximum sample values, with no gain applied.</param>
    /// <param name="gain">The gain the shape is responsible for applying.</param>
    /// <param name="fill">The brush the visualizer is configured to paint with.</param>
    public WaveformRenderContext(
        ImmediateCanvas canvas,
        Rect bounds,
        ReadOnlySpan<float> mins,
        ReadOnlySpan<float> maxs,
        float gain,
        Brush.Resource fill)
    {
        Canvas = canvas;
        Bounds = bounds;
        Mins = mins;
        Maxs = maxs;
        Gain = gain;
        Fill = fill;
    }

    /// <summary>
    /// Gets the canvas to paint on.
    /// </summary>
    public ImmediateCanvas Canvas { get; }

    /// <summary>
    /// Gets the rectangle, in the canvas' coordinate space, the waveform occupies.
    /// </summary>
    public Rect Bounds { get; }

    /// <summary>
    /// Gets the minimum sample value of each slot, in the -1..1 range and with no gain applied.
    /// </summary>
    /// <remarks>
    /// The span is valid only for the duration of the
    /// <see cref="WaveformShape.Resource.Render(in WaveformRenderContext)"/> call.
    /// </remarks>
    public ReadOnlySpan<float> Mins { get; }

    /// <summary>
    /// Gets the maximum sample value of each slot, in the -1..1 range and with no gain applied.
    /// </summary>
    /// <remarks>
    /// The span is valid only for the duration of the
    /// <see cref="WaveformShape.Resource.Render(in WaveformRenderContext)"/> call.
    /// </remarks>
    public ReadOnlySpan<float> Maxs { get; }

    /// <summary>
    /// Gets the gain configured on the visualizer.
    /// </summary>
    /// <remarks>
    /// The shape is responsible for multiplying <see cref="Mins"/> and <see cref="Maxs"/> by this value and
    /// clamping the result back into -1..1; the engine hands the samples over unscaled.
    /// </remarks>
    public float Gain { get; }

    /// <summary>
    /// Gets the brush the visualizer is configured to paint with.
    /// </summary>
    public Brush.Resource Fill { get; }
}
