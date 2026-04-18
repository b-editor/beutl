using Beutl.Audio.Graph;

namespace Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

// Fixed-size ring buffer holding the most recent planar L/R samples together
// with a scene-time anchor so consumers can read samples corresponding to a
// specific playback time — not just the newest sample written.
// Writes may come from ComposeThread; reads happen on the UI thread.
public sealed class AudioSampleRingBuffer
{
    // Gap between a new write's start time and the expected continuation of the
    // previous write. Larger gaps (e.g. scene scrub) trigger a reset so stale
    // samples don't get mixed with data from a different timeline position.
    private static readonly TimeSpan s_continuityTolerance = TimeSpan.FromMilliseconds(150);

    private readonly object _gate = new();
    private float[] _left = [];
    private float[] _right = [];
    private int _capacity;
    private int _writeIndex;
    private long _totalWritten;
    private int _sampleRate;

    // Anchors the ring buffer to scene time: _lastWriteEndTime corresponds to
    // absolute sample index _lastWriteEndIndex (== _totalWritten at that point).
    private bool _hasAnchor;
    private TimeSpan _lastWriteEndTime;
    private long _lastWriteEndIndex;

    public int Capacity
    {
        get
        {
            lock (_gate) return _capacity;
        }
    }

    public int SampleRate
    {
        get
        {
            lock (_gate) return _sampleRate;
        }
    }

    public long TotalWritten
    {
        get
        {
            lock (_gate) return _totalWritten;
        }
    }

    public void Configure(int sampleRate, int capacitySamples)
    {
        if (capacitySamples <= 0) throw new ArgumentOutOfRangeException(nameof(capacitySamples));
        lock (_gate)
        {
            if (_sampleRate == sampleRate && _capacity == capacitySamples) return;
            ResetInternal(sampleRate, capacitySamples);
        }
    }

    public void WriteInterleaved(ReadOnlySpan<float> interleaved, int channelCount, int sampleRate, TimeSpan startTime)
    {
        if (channelCount < 1) return;
        int frames = interleaved.Length / channelCount;
        if (frames == 0) return;

        lock (_gate)
        {
            bool needsReset = _capacity == 0 || _sampleRate != sampleRate;
            if (_hasAnchor && !needsReset)
            {
                // Reset when the incoming chunk is not contiguous with the last write —
                // typical when the user seeks/scrubs during paused compose.
                TimeSpan gap = startTime - _lastWriteEndTime;
                if (gap < -s_continuityTolerance || gap > s_continuityTolerance)
                {
                    needsReset = true;
                }
            }

            if (needsReset)
            {
                // 5s default keeps enough history for the spectrogram view (default window 4s)
                // while staying small (~1MB per channel at 48 kHz).
                int cap = Math.Max(_capacity, sampleRate * 5);
                ResetInternal(sampleRate, cap);
            }

            int capacity = _capacity;
            int idx = _writeIndex;
            for (int f = 0; f < frames; f++)
            {
                int baseIdx = f * channelCount;
                float l = interleaved[baseIdx];
                float r = channelCount > 1 ? interleaved[baseIdx + 1] : l;
                _left[idx] = l;
                _right[idx] = r;
                idx++;
                if (idx >= capacity) idx = 0;
            }
            _writeIndex = idx;
            _totalWritten += frames;

            _hasAnchor = true;
            _lastWriteEndIndex = _totalWritten;
            _lastWriteEndTime = startTime + TimeSpan.FromSeconds(frames / (double)sampleRate);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_left);
            Array.Clear(_right);
            _writeIndex = 0;
            _totalWritten = 0;
            _hasAnchor = false;
            _lastWriteEndIndex = 0;
            _lastWriteEndTime = TimeSpan.Zero;
        }
    }

    // Returns the `length` most recent samples, oldest-first. Used when no
    // playback-time anchor is meaningful (e.g. metering fallback).
    public int ReadLatest(Span<float> destLeft, Span<float> destRight, int length)
    {
        if (destLeft.Length < length || destRight.Length < length) return 0;
        lock (_gate)
        {
            return ReadEndingAtLocked(_totalWritten, destLeft, destRight, length);
        }
    }

    // Reads `length` samples that end exactly at the sample corresponding to
    // `playheadTime` in scene time. Missing samples before/after the available
    // window are zero-padded. Returns the number of real (non-padded) samples.
    public int ReadAroundTime(TimeSpan playheadTime, Span<float> destLeft, Span<float> destRight, int length)
    {
        if (destLeft.Length < length || destRight.Length < length) return 0;
        lock (_gate)
        {
            if (!_hasAnchor || _sampleRate <= 0)
            {
                destLeft.Slice(0, length).Clear();
                destRight.Slice(0, length).Clear();
                return 0;
            }

            double secondsFromEnd = (_lastWriteEndTime - playheadTime).TotalSeconds;
            long samplesFromEnd = (long)Math.Round(secondsFromEnd * _sampleRate);
            long targetEnd = _lastWriteEndIndex - samplesFromEnd;
            return ReadEndingAtLocked(targetEnd, destLeft, destRight, length);
        }
    }

    // Snapshot of `windowSamples` ending at `playheadTime` used for RMS / peak
    // calculations. Falls back to the newest samples if no anchor is available.
    public (float LeftRms, float RightRms, float LeftPeak, float RightPeak) ComputeMeters(
        int windowSamples, TimeSpan? playheadTime = null)
    {
        Span<float> l = windowSamples <= 4096 ? stackalloc float[windowSamples] : new float[windowSamples];
        Span<float> r = windowSamples <= 4096 ? stackalloc float[windowSamples] : new float[windowSamples];
        int got = playheadTime is { } t
            ? ReadAroundTime(t, l, r, windowSamples)
            : ReadLatest(l, r, windowSamples);
        if (got <= 0) return (0, 0, 0, 0);

        ReadOnlySpan<float> lSlice = l.Slice(0, got);
        ReadOnlySpan<float> rSlice = r.Slice(0, got);
        return (
            AudioMath.CalculateRms(lSlice),
            AudioMath.CalculateRms(rSlice),
            AudioMath.FindPeak(lSlice),
            AudioMath.FindPeak(rSlice));
    }

    private int ReadEndingAtLocked(long endAbsIndex, Span<float> destLeft, Span<float> destRight, int length)
    {
        if (_capacity == 0)
        {
            destLeft.Slice(0, length).Clear();
            destRight.Slice(0, length).Clear();
            return 0;
        }

        long oldestAbs = Math.Max(0L, _totalWritten - _capacity);
        if (endAbsIndex > _totalWritten) endAbsIndex = _totalWritten;
        if (endAbsIndex <= oldestAbs)
        {
            destLeft.Slice(0, length).Clear();
            destRight.Slice(0, length).Clear();
            return 0;
        }

        long startAbs = endAbsIndex - length;
        int leadingZeros = 0;
        if (startAbs < oldestAbs)
        {
            leadingZeros = (int)(oldestAbs - startAbs);
            startAbs = oldestAbs;
        }
        if (leadingZeros > 0)
        {
            destLeft.Slice(0, leadingZeros).Clear();
            destRight.Slice(0, leadingZeros).Clear();
        }

        int toCopy = (int)(endAbsIndex - startAbs);
        if (toCopy <= 0) return 0;

        long backFromWriteIdx = _totalWritten - startAbs;
        int ringStart = (int)(((long)_writeIndex - backFromWriteIdx) % _capacity);
        if (ringStart < 0) ringStart += _capacity;

        int firstChunk = Math.Min(toCopy, _capacity - ringStart);
        _left.AsSpan(ringStart, firstChunk).CopyTo(destLeft.Slice(leadingZeros));
        _right.AsSpan(ringStart, firstChunk).CopyTo(destRight.Slice(leadingZeros));

        int remaining = toCopy - firstChunk;
        if (remaining > 0)
        {
            _left.AsSpan(0, remaining).CopyTo(destLeft.Slice(leadingZeros + firstChunk));
            _right.AsSpan(0, remaining).CopyTo(destRight.Slice(leadingZeros + firstChunk));
        }

        // Trailing zero-pad if the request extends past the anchor end.
        int trailingStart = leadingZeros + toCopy;
        if (trailingStart < length)
        {
            destLeft.Slice(trailingStart, length - trailingStart).Clear();
            destRight.Slice(trailingStart, length - trailingStart).Clear();
        }

        return toCopy;
    }

    private void ResetInternal(int sampleRate, int capacity)
    {
        _sampleRate = sampleRate;
        _capacity = capacity;
        _left = new float[capacity];
        _right = new float[capacity];
        _writeIndex = 0;
        _totalWritten = 0;
        _hasAnchor = false;
        _lastWriteEndIndex = 0;
        _lastWriteEndTime = TimeSpan.Zero;
    }
}
