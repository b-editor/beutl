namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

// ITU-R BS.1770-4 K-weighting: a high-shelf pre-filter (+4 dB at 1681 Hz)
// followed by an RLB high-pass at 38 Hz. Coefficients are designed directly
// from the analog prototype + bilinear transform (libebur128 form), giving
// correct response at any sample rate — not only the 48 kHz reference.
internal static class KWeighting
{
    public static void ConfigurePair(int sampleRate, Biquad pre, Biquad rlb)
    {
        DesignPreFilter(sampleRate, pre);
        DesignRlbFilter(sampleRate, rlb);
    }

    private static void DesignPreFilter(int sampleRate, Biquad biquad)
    {
        const double f0 = 1681.974450955533;
        const double G = 3.999843853973347;
        const double Q = 0.7071752369554196;

        double K = Math.Tan(Math.PI * f0 / sampleRate);
        double Vh = Math.Pow(10.0, G / 20.0);
        double Vb = Math.Pow(Vh, 0.4996667741545416);

        double a0 = 1.0 + K / Q + K * K;
        double b0 = (Vh + Vb * K / Q + K * K) / a0;
        double b1 = 2.0 * (K * K - Vh) / a0;
        double b2 = (Vh - Vb * K / Q + K * K) / a0;
        double a1 = 2.0 * (K * K - 1.0) / a0;
        double a2 = (1.0 - K / Q + K * K) / a0;

        biquad.SetCoefficients(b0, b1, b2, a1, a2);
    }

    private static void DesignRlbFilter(int sampleRate, Biquad biquad)
    {
        const double f0 = 38.13547087602444;
        const double Q = 0.5003270373238773;

        double K = Math.Tan(Math.PI * f0 / sampleRate);
        double a0 = 1.0 + K / Q + K * K;

        double a1 = 2.0 * (K * K - 1.0) / a0;
        double a2 = (1.0 - K / Q + K * K) / a0;

        biquad.SetCoefficients(1.0, -2.0, 1.0, a1, a2);
    }
}
