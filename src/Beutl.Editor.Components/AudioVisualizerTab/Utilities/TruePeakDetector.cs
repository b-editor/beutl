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

    public static float Detect(ReadOnlySpan<float> samples)
    {
        float peak = 0f;
        // The sample peak is a floor: the reading never drops below it on blocks too
        // short to interpolate, or through the filter's passband ripple.
        foreach (float s in samples)
        {
            float a = MathF.Abs(s);
            if (a > peak) peak = a;
        }

        for (int n = Taps - 1; n < samples.Length; n++)
        {
            foreach (float[] phase in s_phases)
            {
                float y = 0f;
                for (int k = 0; k < Taps; k++) y += phase[k] * samples[n - k];
                float a = MathF.Abs(y);
                if (a > peak) peak = a;
            }
        }

        return peak;
    }
}
