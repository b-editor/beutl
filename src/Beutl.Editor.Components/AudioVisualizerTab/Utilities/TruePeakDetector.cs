using System.Runtime.Intrinsics;
using Beutl.Audio.Graph;

namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

// 4x oversampled true-peak estimator per ITU-R BS.1770-5 Annex 2: the order-48,
// 4-phase FIR interpolator given there, then the largest absolute value. Only
// positions where all 12 taps of a phase fall inside the block are interpolated;
// a truncated sum of these alternating-sign taps overshoots the signal (#3807).
internal static class TruePeakDetector
{
    private static readonly float[][] s_phases =
    [
        [
            0.0017089843750f, 0.0109863281250f, -0.0196533203125f, 0.0332031250000f, -0.0594482421875f, 0.1373291015625f,
            0.9721679687500f, -0.1022949218750f, 0.0476074218750f, -0.0266113281250f, 0.0148925781250f, -0.0083007812500f
        ],
        [
            -0.0291748046875f, 0.0292968750000f, -0.0517578125000f, 0.0891113281250f, -0.1665039062500f, 0.4650878906250f,
            0.7797851562500f, -0.2003173828125f, 0.1015625000000f, -0.0582275390625f, 0.0330810546875f, -0.0189208984375f
        ],
        [
            -0.0189208984375f, 0.0330810546875f, -0.0582275390625f, 0.1015625000000f, -0.2003173828125f, 0.7797851562500f,
            0.4650878906250f, -0.1665039062500f, 0.0891113281250f, -0.0517578125000f, 0.0292968750000f, -0.0291748046875f
        ],
        [
            -0.0083007812500f, 0.0148925781250f, -0.0266113281250f, 0.0476074218750f, -0.1022949218750f, 0.9721679687500f,
            0.1373291015625f, -0.0594482421875f, 0.0332031250000f, -0.0196533203125f, 0.0109863281250f, 0.0017089843750f
        ],
    ];

    private const int Taps = 12;

    // s_phases by tap, one lane per phase, so a single multiply covers all four phases.
    private static readonly Vector128<float>[] s_tapColumns =
        [.. Enumerable.Range(0, Taps).Select(k => Vector128.Create(s_phases[0][k], s_phases[1][k], s_phases[2][k], s_phases[3][k]))];

    public static float Detect(ReadOnlySpan<float> samples)
    {
        // The sample peak is a floor: the reading never drops below it on blocks too
        // short to interpolate, or through the filter's passband ripple.
        float peak = AudioMath.FindPeak(samples);
        // All zeros interpolate to zero, and a NaN never reads above the floor.
        if (peak == 0f) return 0f;

        // Plain multiplies and adds in tap order: a fused form rounds differently and would change the reading.
        Vector128<float>[] columns = s_tapColumns;
        Vector128<float> peaks = Vector128<float>.Zero;
        for (int n = Taps - 1; n < samples.Length; n++)
        {
            Vector128<float> y = Vector128<float>.Zero;
            for (int k = 0; k < Taps; k++) y += columns[k] * Vector128.Create(samples[n - k]);
            Vector128<float> a = Vector128.Abs(y);
            peaks = Vector128.ConditionalSelect(Vector128.GreaterThan(a, peaks), a, peaks);
        }

        for (int i = 0; i < Vector128<float>.Count; i++)
        {
            if (peaks[i] > peak) peak = peaks[i];
        }

        return peak;
    }
}
