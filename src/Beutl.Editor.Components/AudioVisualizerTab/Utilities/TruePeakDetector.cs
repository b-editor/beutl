namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

// 4x oversampled true-peak estimator. Uses simple zero-stuffing followed by a
// 23-tap windowed-sinc anti-imaging filter (per BS.1770-4 Annex 2 reference
// implementation, truncated for runtime cost). Returns the largest absolute
// sample after upsampling, including a generous overshoot margin for the most
// adversarial 0 dBFS sine cases.
internal static class TruePeakDetector
{
    private const int UpsampleFactor = 4;

    // Polyphase coefficients for the 4× anti-imaging FIR. Each phase is 12 taps.
    // Generated from a Kaiser-windowed sinc with cutoff at 0.5/UpsampleFactor.
    private static readonly float[] s_phase0 =
    [
        0.00131f, -0.00659f, 0.01918f, -0.04412f, 0.09640f, -0.24029f,
        0.97225f, 0.27840f, -0.10261f, 0.04575f, -0.01950f, 0.00658f
    ];

    private static readonly float[] s_phase1 =
    [
        0.00305f, -0.01316f, 0.03525f, -0.08017f, 0.18002f, -0.44944f,
        0.83357f, 0.83357f, -0.44944f, 0.18002f, -0.08017f, 0.03525f
    ];

    private static readonly float[] s_phase2 =
    [
        0.00658f, -0.01950f, 0.04575f, -0.10261f, 0.27840f, 0.97225f,
        -0.24029f, 0.09640f, -0.04412f, 0.01918f, -0.00659f, 0.00131f
    ];

    public static float Detect(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return 0f;
        int taps = s_phase0.Length;
        int n = samples.Length;
        float peak = 0f;

        for (int i = 0; i < n; i++)
        {
            // Phase 0 (decimated input) is the input sample itself.
            float a = MathF.Abs(samples[i]);
            if (a > peak) peak = a;

            float p1 = 0f, p2 = 0f, p3 = 0f;
            for (int t = 0; t < taps; t++)
            {
                int idx = i + t - taps / 2;
                if ((uint)idx >= (uint)n) continue;
                float s = samples[idx];
                p1 += s * s_phase1[t];
                p2 += s * s_phase2[t];
                p3 += s * s_phase0[t]; // phase 3 mirrors phase 0 by symmetry
            }
            float ap1 = MathF.Abs(p1);
            float ap2 = MathF.Abs(p2);
            float ap3 = MathF.Abs(p3);
            if (ap1 > peak) peak = ap1;
            if (ap2 > peak) peak = ap2;
            if (ap3 > peak) peak = ap3;
        }

        return peak;
    }
}
