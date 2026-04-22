namespace Beutl.Editor.Components.AudioVisualizerTab.Utilities;

// Computes BS.1770 momentary loudness on a stereo signal. K-weighted mean
// square is summed across L/R with channel weights of 1.0 each (per Annex 1).
// Loudness = -0.691 + 10·log10(sum). Designed to be called per render with
// the most recent ~400 ms of samples; coefficients are reset whenever the
// sample rate changes.
internal sealed class LoudnessMeter
{
    private readonly Biquad _preL = new();
    private readonly Biquad _rlbL = new();
    private readonly Biquad _preR = new();
    private readonly Biquad _rlbR = new();

    private float[] _scratch = [];
    private int _configuredRate;

    public void Reconfigure(int sampleRate)
    {
        if (sampleRate == _configuredRate) return;
        KWeighting.ConfigurePair(sampleRate, _preL, _rlbL);
        KWeighting.ConfigurePair(sampleRate, _preR, _rlbR);
        _preL.Reset();
        _rlbL.Reset();
        _preR.Reset();
        _rlbR.Reset();
        _configuredRate = sampleRate;
    }

    // Returns LUFS (LKFS) for the given block. Returns -160 LUFS when the
    // block is silent (sum == 0) so callers can clamp to a display floor.
    public float Compute(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
    {
        if (left.Length == 0 || right.Length != left.Length) return -160f;
        Reconfigure(sampleRate);

        int n = left.Length;
        if (_scratch.Length < n) _scratch = new float[n];
        Span<float> tmp = _scratch.AsSpan(0, n);

        // Reset filter state per call. Windows passed here are usually heavily
        // overlapping (or identical while paused), so carrying IIR state across
        // calls would make the reading depend on repaint cadence instead of the
        // samples in the current window. The biquad start-up transient settles
        // within a handful of samples and is negligible over a 400 ms block.
        _preL.Reset();
        _rlbL.Reset();
        _preR.Reset();
        _rlbR.Reset();

        _preL.Process(left, tmp);
        _rlbL.Process(tmp, tmp);
        double sum = 0;
        for (int i = 0; i < n; i++) sum += tmp[i] * (double)tmp[i];

        _preR.Process(right, tmp);
        _rlbR.Process(tmp, tmp);
        for (int i = 0; i < n; i++) sum += tmp[i] * (double)tmp[i];

        double meanSquare = sum / n;
        if (meanSquare <= 0) return -160f;
        return (float)(-0.691 + 10.0 * Math.Log10(meanSquare));
    }
}
