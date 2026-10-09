using System.Numerics;

namespace Beutl.Audio.Graph;

/// <summary>Per-frame controls of <see cref="SpectralNoiseReducer"/>, already clamped to their ranges.</summary>
internal readonly record struct NoiseReductionSettings(
    float ReductionDb,
    float SensitivityDb,
    float Smoothing,
    float AdaptationSeconds);

// Short-time spectral noise reduction for a stereo stream addressed by absolute sample positions.
// Both channels share one complex FFT per 75%-overlapped Hann frame (left in the real part, right in
// the imaginary part) and one gain per bin, so suppression cannot move the stereo image. Minimum
// statistics track the noise floor, and a two-step decision-directed Wiener rule turns it into gains no
// lower than the requested reduction. The filtered difference is added to the dry input, so unity gain
// reproduces the input exactly.
internal sealed class SpectralNoiseReducer
{
    // The noise floor is the bias-corrected minimum of the smoothed power over this many sub-windows,
    // which together span the adaptation time.
    private const int SubWindowCount = 8;

    // Per-frame smoothing of the power the minimum is taken from (about 40 ms at a 5.3 ms hop).
    private const float PowerSmoothing = 0.875f;

    private const float MinPriorSnr = 1e-4f;
    private const float MaxPriorSnr = 1e20f;
    private const float MinNoisePower = 1e-30f;

    // A frame whose real content carries less window energy than this is treated as silence.
    private const double MinFrameEnergyFraction = 1e-3;

    // A fresh recursive average still remembers its first periodogram this long; the minimum skips
    // those frames, because a single periodogram can sit far below the mean.
    private static readonly int s_warmUpFrames = (int)Math.Ceiling(Math.Log(0.1) / Math.Log(PowerSmoothing));

    private readonly int _frameSize;
    private readonly int _hopSize;
    private readonly int _bins;
    private readonly double _hopSeconds;
    private readonly float[] _window;
    private readonly double[] _windowEnergy;
    private readonly float _synthesisScale;

    private readonly float[] _real;
    private readonly float[] _imag;
    private readonly float[] _power;
    private readonly float[] _smoothedPower;
    private readonly float[] _subWindowMin;
    private readonly float[] _subWindowMins;

    // The frames each stored minimum covers. A changed adaptation time alters only the sub-windows
    // collected after it, so the bias must follow the frames actually stored.
    private readonly int[] _subWindowLengths;
    private readonly float[] _noise;
    private readonly float[] _cleanPower;
    private readonly float[] _gain;
    private readonly float[] _overlapLeft;
    private readonly float[] _overlapRight;
    private readonly float[] _blockLeft;
    private readonly float[] _blockRight;

    private float[] _inputLeft;
    private float[] _inputRight;
    private long _inputBase;
    private long _inputEnd;
    private long _origin;
    private long _paddingStart;
    private long _nextFrame;
    private long _outputPosition;
    private long _blockPosition;
    private int _blockOffset;
    private int _blockCount;

    private int _trackedFrames;
    private long _trackedUntil;
    private bool _seeded;
    private bool _hasCleanPower;
    private int _subWindowFrames;
    private int _ringCount;
    private int _ringIndex;

    public SpectralNoiseReducer(int sampleRate)
    {
        _frameSize = GetFrameSize(sampleRate);
        _hopSize = _frameSize / 4;
        _bins = _frameSize / 2 + 1;
        _hopSeconds = _hopSize / (double)sampleRate;

        _window = new float[_frameSize];
        _windowEnergy = new double[_frameSize + 1];
        for (int n = 0; n < _frameSize; n++)
        {
            double w = 0.5 * (1 - Math.Cos(2 * Math.PI * n / _frameSize));
            _window[n] = (float)w;
            _windowEnergy[n + 1] = _windowEnergy[n] + w * w;
        }

        // The periodic Hann window applied twice overlap-adds to exactly 1.5 at a quarter-frame hop,
        // and the inverse transform below is unnormalized.
        _synthesisScale = 1f / (_frameSize * 1.5f);

        _real = new float[_frameSize];
        _imag = new float[_frameSize];
        _power = new float[_bins];
        _smoothedPower = new float[_bins];
        _subWindowMin = new float[_bins];
        _subWindowMins = new float[_bins * SubWindowCount];
        _subWindowLengths = new int[SubWindowCount];
        _noise = new float[_bins];
        _cleanPower = new float[_bins];
        _gain = new float[_bins];
        _overlapLeft = new float[_frameSize];
        _overlapRight = new float[_frameSize];
        _blockLeft = new float[_hopSize];
        _blockRight = new float[_hopSize];
        _inputLeft = new float[_frameSize * 2];
        _inputRight = new float[_frameSize * 2];

        Reset(0, 0);
    }

    public int FrameSize => _frameSize;

    public int HopSize => _hopSize;

    /// <summary>The absolute position after the last written input sample.</summary>
    public long InputEnd => _inputEnd;

    /// <summary>The absolute position of the next output sample <see cref="Read"/> produces.</summary>
    public long OutputPosition => _outputPosition;

    /// <summary>The current noise power per bin, on the scale of a Hann-windowed periodogram.</summary>
    internal ReadOnlySpan<float> NoiseEstimate => _noise;

    // At least 20 ms, rounded up to a power of two for the radix-2 FFT: 1024 at 44.1 and 48 kHz.
    internal static int GetFrameSize(int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);
        return (int)BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(sampleRate / 50, 256, 8192));
    }

    /// <summary>
    /// Starts a new stream whose input begins at <paramref name="origin"/>, a multiple of the hop size,
    /// and whose output begins at <paramref name="outputStart"/>. Input before the origin is silence,
    /// and outputs between the origin and the output start are computed and discarded. With
    /// <paramref name="keepNoiseFloor"/>, the learned noise floor and gain history survive, and frames
    /// replayed from the same timeline are not counted again.
    /// </summary>
    public void Reset(long origin, long outputStart, bool keepNoiseFloor = false)
    {
        if (FloorMod(origin, _hopSize) != 0)
            throw new ArgumentException("The origin must lie on the hop grid.", nameof(origin));
        ArgumentOutOfRangeException.ThrowIfLessThan(outputStart, origin);

        int history = _frameSize - _hopSize;
        _origin = origin;
        _inputBase = origin - history;
        _inputEnd = origin;
        _paddingStart = long.MaxValue;
        _nextFrame = _inputBase;
        _outputPosition = outputStart;
        Array.Clear(_inputLeft, 0, history);
        Array.Clear(_inputRight, 0, history);
        Array.Clear(_overlapLeft);
        Array.Clear(_overlapRight);
        _blockOffset = 0;
        _blockCount = 0;
        if (keepNoiseFloor)
            return;

        _trackedFrames = 0;
        _trackedUntil = long.MinValue;
        _seeded = false;
        _hasCleanPower = false;
        _subWindowFrames = 0;
        _ringCount = 0;
        _ringIndex = 0;
        Array.Fill(_subWindowMin, float.PositiveInfinity);
        Array.Clear(_subWindowLengths);
        Array.Clear(_noise);
    }

    /// <summary>Appends interleaved stereo input. Non-finite samples are replaced with silence.</summary>
    public void Write(ReadOnlySpan<float> interleaved)
    {
        if ((interleaved.Length & 1) != 0)
            throw new ArgumentException("Interleaved stereo input must contain whole frames.", nameof(interleaved));
        if (_paddingStart != long.MaxValue)
            throw new InvalidOperationException("Cannot append audio after the stream was padded with silence.");

        int count = interleaved.Length / 2;
        int offset = PrepareWrite(count);
        Span<float> left = _inputLeft.AsSpan(offset, count);
        Span<float> right = _inputRight.AsSpan(offset, count);
        for (int i = 0; i < count; i++)
        {
            float l = interleaved[i * 2];
            float r = interleaved[i * 2 + 1];
            left[i] = float.IsFinite(l) ? l : 0f;
            right[i] = float.IsFinite(r) ? r : 0f;
        }

        _inputEnd += count;
    }

    /// <summary>
    /// Appends silence that marks the end of the stream. Frames that reach into it are judged by their
    /// real content only and no longer update the noise floor.
    /// </summary>
    public void WritePadding(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_paddingStart == long.MaxValue)
            _paddingStart = _inputEnd;

        int offset = PrepareWrite(count);
        Array.Clear(_inputLeft, offset, count);
        Array.Clear(_inputRight, offset, count);
        _inputEnd += count;
    }

    /// <summary>The input end <see cref="Read"/> needs before it can produce output up to <paramref name="outputEnd"/>.</summary>
    public long GetRequiredInputEnd(long outputEnd)
    {
        // Every frame that starts before outputEnd contributes to it; the last of them ends a frame later.
        return outputEnd <= _outputPosition
            ? _inputBase
            : FloorToHop(outputEnd - 1) + _frameSize;
    }

    /// <summary>
    /// Starts the noise floor from the complete frames between the origin and <paramref name="end"/>.
    /// A stream that begins in the middle of speech would otherwise treat the first syllable as noise
    /// until a pause arrives. Does nothing when too little input is available.
    /// </summary>
    public void Seed(long end)
    {
        long limit = Math.Min(Math.Min(end, _inputEnd), _paddingStart);
        Span<float> minimum = _subWindowMin;
        minimum.Fill(float.PositiveInfinity);
        int frames = 0;
        long lastFrame = long.MinValue;
        for (long start = _origin; start + _frameSize <= limit; start += _hopSize)
        {
            AnalyzeFrame(start);
            lastFrame = start;
            if (frames == 0)
            {
                _power.CopyTo(_smoothedPower, 0);
            }
            else
            {
                SmoothPower();
            }

            if (++frames > s_warmUpFrames)
            {
                for (int k = 0; k < _bins; k++)
                    minimum[k] = MathF.Min(minimum[k], _smoothedPower[k]);
            }
        }

        int counted = frames - s_warmUpFrames;
        if (counted <= 0)
        {
            minimum.Fill(float.PositiveInfinity);
            return;
        }

        // The seed becomes one stored minimum over the frames it saw and ages out of the window like
        // any other sub-window.
        minimum.CopyTo(_subWindowMins);
        _subWindowLengths[0] = counted;
        minimum.Fill(float.PositiveInfinity);
        _ringCount = 1;
        _ringIndex = 1;
        _subWindowFrames = 0;
        _seeded = true;

        // Reading revisits these frames from the origin; counting them again would double the
        // evidence the bias assumes. The smoothed power continues from the last of them.
        _trackedUntil = lastFrame;
        UpdateNoise();
    }

    /// <summary>
    /// Ends the real input at <paramref name="end"/>, between <see cref="OutputPosition"/> and
    /// <see cref="InputEnd"/>. Input after it becomes silence, and the frames that reach the output
    /// position are recomputed from the input kept before it, so no later audio leaks into the output.
    /// The noise floor is kept.
    /// </summary>
    public void EndInputAt(long end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(end, _outputPosition);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(end, _inputEnd);

        // Frames that start a full frame before the output position never reach it, and PrepareWrite
        // keeps a frame of input before the next frame for exactly this replay.
        long replay = FloorToHop(_outputPosition) - (_frameSize - _hopSize);
        if (replay < _inputBase)
            throw new InvalidOperationException("The input needed to recompute the output was discarded.");

        long cut = Math.Min(end, _paddingStart);
        int count = (int)Math.Max(0, cut - replay);
        int offset = (int)(replay - _inputBase);
        var interleaved = new float[count * 2];
        for (int i = 0; i < count; i++)
        {
            interleaved[i * 2] = _inputLeft[offset + i];
            interleaved[i * 2 + 1] = _inputRight[offset + i];
        }

        Reset(replay, _outputPosition, keepNoiseFloor: true);
        Write(interleaved);
        WritePadding(0);
    }

    /// <summary>
    /// Produces the next output samples. <paramref name="settingsAt"/> receives the absolute position
    /// of each frame's center. Throws when <see cref="GetRequiredInputEnd"/> has not been satisfied.
    /// </summary>
    public void Read(Span<float> left, Span<float> right, Func<long, NoiseReductionSettings> settingsAt)
    {
        ArgumentNullException.ThrowIfNull(settingsAt);
        if (left.Length != right.Length)
            throw new ArgumentException("Both channels must have the same length.", nameof(right));

        int count = left.Length;
        if (count == 0)
            return;
        if (_inputEnd < GetRequiredInputEnd(_outputPosition + count))
            throw new InvalidOperationException("The reducer needs more input before it can produce this output.");

        int written = 0;
        while (written < count)
        {
            if (_blockCount == 0)
            {
                ProcessFrame(settingsAt(_nextFrame + _frameSize / 2));
                continue;
            }

            // A block can begin before the output when the stream was reset ahead of it.
            long blockPosition = _blockPosition + _blockOffset;
            if (blockPosition < _outputPosition)
            {
                int skip = (int)Math.Min(_blockCount, _outputPosition - blockPosition);
                _blockOffset += skip;
                _blockCount -= skip;
                continue;
            }

            int n = Math.Min(_blockCount, count - written);
            _blockLeft.AsSpan(_blockOffset, n).CopyTo(left.Slice(written, n));
            _blockRight.AsSpan(_blockOffset, n).CopyTo(right.Slice(written, n));
            _blockOffset += n;
            _blockCount -= n;
            written += n;
            _outputPosition += n;
        }
    }

    private void ProcessFrame(in NoiseReductionSettings settings)
    {
        long start = _nextFrame;
        double fraction = AnalyzeFrame(start);
        if (fraction >= MinFrameEnergyFraction)
        {
            // Only frames made entirely of real input describe the noise floor, each one once.
            if (fraction >= 1 && start > _trackedUntil)
            {
                TrackNoise(settings.AdaptationSeconds);
                _trackedUntil = start;
            }

            if (ComputeGains(settings))
                ApplyCorrection();
        }

        EmitBlock(start);
        _nextFrame = start + _hopSize;
    }

    // Windows and transforms the frame at start, then stores the mean power of the two channels per bin.
    // Returns the share of the window's energy that falls on real input; the power is normalized by it,
    // so a frame cut by the start or end of the stream is judged by the audio it does contain.
    private double AnalyzeFrame(long start)
    {
        int offset = (int)(start - _inputBase);
        for (int n = 0; n < _frameSize; n++)
        {
            float w = _window[n];
            _real[n] = w * _inputLeft[offset + n];
            _imag[n] = w * _inputRight[offset + n];
        }

        Fft.Forward(_real, _imag);

        long validStart = Math.Max(start, _origin);
        long validEnd = Math.Min(start + _frameSize, _paddingStart);
        double fraction = validEnd > validStart
            ? (_windowEnergy[validEnd - start] - _windowEnergy[validStart - start]) / _windowEnergy[_frameSize]
            : 0;
        if (fraction < MinFrameEnergyFraction)
            return fraction;

        // With z = l + i r, |L[k]|^2 + |R[k]|^2 = (|Z[k]|^2 + |Z[N-k]|^2) / 2.
        float scale = (float)(0.25 / fraction);
        int mask = _frameSize - 1;
        for (int k = 0; k < _bins; k++)
        {
            int mirror = (_frameSize - k) & mask;
            float re = _real[k];
            float im = _imag[k];
            float mirrorRe = _real[mirror];
            float mirrorIm = _imag[mirror];
            _power[k] = (re * re + im * im + mirrorRe * mirrorRe + mirrorIm * mirrorIm) * scale;
        }

        return fraction;
    }

    private void SmoothPower()
    {
        for (int k = 0; k < _bins; k++)
            _smoothedPower[k] = PowerSmoothing * _smoothedPower[k] + (1 - PowerSmoothing) * _power[k];
    }

    private void TrackNoise(float adaptationSeconds)
    {
        if (_seeded || _trackedFrames > 0)
        {
            SmoothPower();
        }
        else
        {
            _power.CopyTo(_smoothedPower, 0);
        }

        _trackedFrames++;
        if (!_seeded && _trackedFrames <= s_warmUpFrames)
            return;

        for (int k = 0; k < _bins; k++)
            _subWindowMin[k] = MathF.Min(_subWindowMin[k], _smoothedPower[k]);

        if (++_subWindowFrames >= GetSubWindowLength(adaptationSeconds))
        {
            _subWindowMin.CopyTo(_subWindowMins, _ringIndex * _bins);
            _subWindowLengths[_ringIndex] = _subWindowFrames;
            _ringIndex = (_ringIndex + 1) % SubWindowCount;
            _ringCount = Math.Min(_ringCount + 1, SubWindowCount);
            Array.Fill(_subWindowMin, float.PositiveInfinity);
            _subWindowFrames = 0;
        }

        UpdateNoise();
    }

    private void UpdateNoise()
    {
        int frames = _subWindowFrames;
        for (int u = 0; u < _ringCount; u++)
            frames += _subWindowLengths[u];

        if (frames == 0)
        {
            Array.Clear(_noise);
            return;
        }

        float bias = GetMinimumBias(frames);
        for (int k = 0; k < _bins; k++)
        {
            float minimum = _subWindowFrames > 0 ? _subWindowMin[k] : float.PositiveInfinity;
            for (int u = 0; u < _ringCount; u++)
                minimum = MathF.Min(minimum, _subWindowMins[u * _bins + k]);

            _noise[k] = minimum * bias;
        }
    }

    // Returns whether any bin is attenuated.
    private bool ComputeGains(in NoiseReductionSettings settings)
    {
        float floor = MathF.Pow(10f, -settings.ReductionDb / 20f);
        float overestimate = MathF.Pow(10f, settings.SensitivityDb / 10f);

        // Smoothing sets how long the a priori SNR remembers earlier frames, from 0.1 s to 2 s. Longer
        // memory keeps random noise peaks from opening the gain, which would sound as warbling.
        float priorWeight = (float)Math.Exp(-_hopSeconds / (0.1 * Math.Pow(20, settings.Smoothing / 100f)));

        bool attenuates = false;
        for (int k = 0; k < _bins; k++)
        {
            float power = _power[k];
            float noise = _noise[k] * overestimate;
            float gain = 1f;

            // Without a noise estimate there is nothing to tell apart from the signal.
            if (floor < 1f && noise > MinNoisePower)
            {
                float posterior = power / noise;
                float instantaneous = MathF.Max(posterior - 1f, 0f);
                float prior = _hasCleanPower
                    ? priorWeight * (_cleanPower[k] / noise) + (1f - priorWeight) * instantaneous
                    : instantaneous;
                prior = Math.Clamp(prior, MinPriorSnr, MaxPriorSnr);

                // The decision-directed estimate lags the signal by a frame. Re-estimating the SNR from
                // the current frame filtered by that gain removes the lag, so onsets and consonants
                // survive.
                float filtered = prior / (1f + prior);
                prior = MathF.Min(filtered * filtered * posterior, MaxPriorSnr);
                gain = MathF.Max(prior / (1f + prior), floor);
                attenuates |= gain < 1f;
            }

            _gain[k] = gain;
            _cleanPower[k] = gain * gain * power;
        }

        _hasCleanPower = true;
        return attenuates;
    }

    // Overlap-adds the time-domain difference between the filtered and the dry frame.
    private void ApplyCorrection()
    {
        int half = _frameSize / 2;
        for (int k = 0; k < _frameSize; k++)
        {
            // Bins k and N-k share a gain, so each channel's correction stays real.
            float difference = _gain[k <= half ? k : _frameSize - k] - 1f;
            _real[k] *= difference;

            // Conjugating before and after the forward transform computes the inverse transform.
            _imag[k] *= -difference;
        }

        Fft.Forward(_real, _imag);

        for (int n = 0; n < _frameSize; n++)
        {
            float w = _window[n] * _synthesisScale;
            _overlapLeft[n] += w * _real[n];
            _overlapRight[n] -= w * _imag[n];
        }
    }

    private void EmitBlock(long start)
    {
        // No later frame overlaps [start, start + hop), so these samples are final.
        if (start + _hopSize > _outputPosition)
        {
            int offset = (int)(start - _inputBase);
            for (int i = 0; i < _hopSize; i++)
            {
                _blockLeft[i] = _inputLeft[offset + i] + _overlapLeft[i];
                _blockRight[i] = _inputRight[offset + i] + _overlapRight[i];
            }

            _blockPosition = start;
            _blockOffset = 0;
            _blockCount = _hopSize;
        }

        int remaining = _frameSize - _hopSize;
        Array.Copy(_overlapLeft, _hopSize, _overlapLeft, 0, remaining);
        Array.Copy(_overlapRight, _hopSize, _overlapRight, 0, remaining);
        Array.Clear(_overlapLeft, remaining, _hopSize);
        Array.Clear(_overlapRight, remaining, _hopSize);
    }

    // Returns the offset in the input arrays where count new samples go.
    private int PrepareWrite(int count)
    {
        int used = checked((int)(_inputEnd - _inputBase));
        int required = checked(used + count);
        if (required <= _inputLeft.Length)
            return used;

        // Input before the next frame has been emitted. One frame of it stays for EndInputAt.
        long discard = _nextFrame - _frameSize - _inputBase;
        if (discard > 0)
        {
            int shift = (int)discard;
            Array.Copy(_inputLeft, shift, _inputLeft, 0, used - shift);
            Array.Copy(_inputRight, shift, _inputRight, 0, used - shift);
            _inputBase += shift;
            used -= shift;
            required -= shift;
        }

        if (required > _inputLeft.Length)
        {
            int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)required);
            Array.Resize(ref _inputLeft, capacity);
            Array.Resize(ref _inputRight, capacity);
        }

        return used;
    }

    private int GetSubWindowLength(float adaptationSeconds)
        => Math.Max(1, (int)Math.Round(adaptationSeconds / (SubWindowCount * _hopSeconds)));

    // The minimum of the smoothed power over a window of frames sits below the mean noise power;
    // this factor restores the mean. Measured on Gaussian noise for windows of 2^i frames, it is the
    // geometric mean of identical channels (2.52 over a 2 s window) and independent ones (1.92), so
    // either estimate is within 0.6 dB.
    internal static float GetMinimumBias(int frames)
    {
        ReadOnlySpan<float> bias =
        [
            1.0000f, 1.0417f, 1.1102f, 1.2112f, 1.3482f, 1.5169f, 1.7042f,
            1.8971f, 2.0887f, 2.2763f, 2.4605f, 2.6422f, 2.8230f,
        ];

        // The bias grows linearly with the logarithm of the window length.
        double index = Math.Log2(Math.Max(frames, 1));
        int lower = Math.Min((int)index, bias.Length - 2);
        double fraction = index - lower;
        return (float)(bias[lower] + (bias[lower + 1] - bias[lower]) * fraction);
    }

    private long FloorToHop(long position) => position - FloorMod(position, _hopSize);

    private static long FloorMod(long value, int divisor)
    {
        long remainder = value % divisor;
        return remainder < 0 ? remainder + divisor : remainder;
    }
}
