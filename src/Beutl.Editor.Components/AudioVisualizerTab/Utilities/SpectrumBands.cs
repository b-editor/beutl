namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

// Analysis steps shared by the spectrum and spectrogram views.
internal static class SpectrumBands
{
    public static void MixToMono(ReadOnlySpan<float> left, ReadOnlySpan<float> right, Span<float> mono)
    {
        for (int i = 0; i < mono.Length; i++)
        {
            mono[i] = 0.5f * (left[i] + right[i]);
        }
    }

    // Logarithmic frequency layout: band b of B covers bins [bins^(b/B), bins^((b+1)/B)), skipping the DC bin
    // and never narrower than one bin while one is left.
    public static void GetBinRange(int bins, int band, int bands, out int start, out int end)
    {
        double lo = Math.Pow(bins, band / (double)bands);
        double hi = Math.Pow(bins, (band + 1) / (double)bands);
        start = Math.Max(1, (int)Math.Floor(lo));
        end = Math.Min(bins, (int)Math.Ceiling(hi));
        if (end <= start) end = Math.Min(bins, start + 1);
    }
}
