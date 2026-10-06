using System.Runtime.CompilerServices;

namespace Beutl.Graphics.AudioVisualizers;

/// <summary>
/// The per-sample arithmetic the waveform shapes share.
/// </summary>
internal static class WaveformSampleMath
{
    /// <summary>
    /// The larger excursion of a min/max sample pair from zero, scaled by <paramref name="gain"/> and clamped to
    /// [0, 1].
    /// </summary>
    /// <remarks>It runs once per drawn bar or envelope point, so it is inlined into the shapes' loops.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float PeakMagnitude(float min, float max, float gain)
        => Math.Clamp(MathF.Max(MathF.Abs(min), MathF.Abs(max)) * gain, 0f, 1f);
}
