using Beutl.Audio;

namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

// Computes BS.1770 momentary loudness. The K-weighted mean square of each channel
// is scaled by its channel weight G_i and summed; Loudness = -0.691 + 10·log10(sum).
// Weights follow BS.1770-5: Annex 1 Table 3 for 5.1 and Annex 3 Tables 4/5 for 7.1
// (M±090 and M±110 surrounds get 1.41, M±135 rears 1.0); LFE is excluded.
// Designed to be called per render with the most recent ~400 ms of samples;
// coefficients are reset whenever the sample rate changes.
internal sealed class LoudnessMeter
{
    private const double SurroundWeight = 1.41;

    private readonly Biquad _pre = new();
    private readonly Biquad _rlb = new();

    private float[] _scratch = [];
    private int _configuredRate;

    public void Reconfigure(int sampleRate)
    {
        if (sampleRate == _configuredRate) return;
        KWeighting.ConfigurePair(sampleRate, _pre, _rlb);
        ResetFilters();
        _configuredRate = sampleRate;
    }

    // Returns LUFS (LKFS) for the given stereo block, both channels weighted 1.0.
    // Returns -160 LUFS when the block is silent (sum == 0) so callers can clamp to
    // a display floor.
    public float Compute(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
    {
        if (left.Length == 0 || right.Length != left.Length) return -160f;
        Reconfigure(sampleRate);

        double sum = SumWeightedSquares(left, 1.0) + SumWeightedSquares(right, 1.0);
        return ToLufs(sum / left.Length);
    }

    // Multichannel variant: reads `count` samples from `offset` of each channel and
    // weights them by their position in `layout`.
    public float Compute(float[][] channels, int offset, int count, AudioChannelLayout layout, int sampleRate)
    {
        if (count <= 0 || channels.Length != layout.ChannelCount) return -160f;
        Reconfigure(sampleRate);

        double sum = 0;
        for (int ch = 0; ch < channels.Length; ch++)
        {
            double weight = GetChannelWeight(layout, ch);
            if (weight == 0) continue;
            sum += SumWeightedSquares(channels[ch].AsSpan(offset, count), weight);
        }

        return ToLufs(sum / count);
    }

    public static double GetChannelWeight(AudioChannelLayout layout, int channel) => layout.GetSpeaker(channel) switch
    {
        AudioSpeakers.LowFrequency => 0,
        AudioSpeakers.SideLeft or AudioSpeakers.SideRight => SurroundWeight,
        // 5.1 puts its surrounds on the back bits (±110°); with side channels present
        // the back pair is the 7.1 rear (±135°), which BS.1770 weights 1.0.
        AudioSpeakers.BackLeft => (layout.Speakers & AudioSpeakers.SideLeft) != 0 ? 1.0 : SurroundWeight,
        AudioSpeakers.BackRight => (layout.Speakers & AudioSpeakers.SideRight) != 0 ? 1.0 : SurroundWeight,
        _ => 1.0,
    };

    private double SumWeightedSquares(ReadOnlySpan<float> samples, double weight)
    {
        int n = samples.Length;
        if (_scratch.Length < n) _scratch = new float[n];
        Span<float> tmp = _scratch.AsSpan(0, n);

        // Reset the filter state per channel and call. Windows overlap between repaints,
        // so carrying the state over would tie the reading to the repaint cadence.
        ResetFilters();
        _pre.Process(samples, tmp);
        _rlb.Process(tmp, tmp);

        double sum = 0;
        for (int i = 0; i < n; i++) sum += tmp[i] * (double)tmp[i];
        return sum * weight;
    }

    private static float ToLufs(double meanSquare)
    {
        if (meanSquare <= 0) return -160f;
        return (float)(-0.691 + 10.0 * Math.Log10(meanSquare));
    }

    private void ResetFilters()
    {
        _pre.Reset();
        _rlb.Reset();
    }
}
