using Beutl.Audio;
using Beutl.Audio.Graph;

namespace Beutl.Editor.Components.AudioVisualizerTab.ViewModels;

// Fixed-size ring buffer holding the most recent planar L/R samples together
// with a scene-time anchor so consumers can read samples corresponding to a
// specific playback time — not just the newest sample written. Sources with
// more than two channels are folded to L/R for the stereo views and also kept
// per channel for the surround meter.
// Writes may come from ComposeThread; reads happen on the UI thread.
public sealed class AudioSampleRingBuffer
{
    // Gap between a new write's start time and the expected continuation of the
    // previous write. Larger gaps (e.g. scene scrub) trigger a reset so stale
    // samples don't get mixed with data from a different timeline position.
    private static readonly MediaTime s_continuityTolerance = MediaTime.FromSeconds(0.15);

    private readonly object _gate = new();
    private float[] _left = [];
    private float[] _right = [];
    private float[][] _channels = [];
    private float[][] _sourceChannels = [];
    private int _channelCount;
    private int _capacity;
    private int _writeIndex;
    private long _totalWritten;
    private int _sampleRate;

    // Anchors the ring buffer to scene time: _lastWriteEndTime corresponds to
    // absolute sample index _lastWriteEndIndex (== _totalWritten at that point).
    private bool _hasAnchor;
    private MediaTime _lastWriteEndTime;
    private long _lastWriteEndIndex;

    public int SampleRate
    {
        get
        {
            lock (_gate) return _sampleRate;
        }
    }

    // Channel count of the last write, before folding. Zero until the first write.
    public int ChannelCount
    {
        get
        {
            lock (_gate) return _channelCount;
        }
    }

    public void WriteInterleaved(ReadOnlySpan<float> interleaved, int channelCount, int sampleRate, MediaTime startTime)
    {
        if (channelCount < 1) return;
        int frames = interleaved.Length / channelCount;
        if (frames == 0) return;
        AudioChannelMatrix? matrix = channelCount > 2
            ? AudioChannelMatrix.Create(AudioChannelLayout.GetDefault(channelCount), AudioChannelLayout.Stereo)
            : null;

        lock (_gate)
        {
            bool needsReset = _capacity == 0 || _sampleRate != sampleRate || _channelCount != channelCount;
            if (_hasAnchor && !needsReset)
            {
                // Any backward overlap (paused frame-step composes a fixed 100 ms
                // window ending at the playhead, so stepping by <100 ms always
                // overlaps) or a large forward jump breaks the linear time↔index
                // mapping that ReadAroundTime relies on — reset in those cases.
                MediaTime gap = startTime - _lastWriteEndTime;
                if (gap < MediaTime.Zero || gap > s_continuityTolerance)
                {
                    needsReset = true;
                }
            }

            if (needsReset)
            {
                // 5s default keeps enough history for the spectrogram view (default window 4s)
                // while staying small (~1MB per channel at 48 kHz).
                int cap = Math.Max(_capacity, sampleRate * 5);
                ResetInternal(sampleRate, cap, channelCount);
            }

            int capacity = _capacity;
            int idx = _writeIndex;
            for (int f = 0; f < frames; f++)
            {
                int baseIdx = f * channelCount;
                float l = interleaved[baseIdx];
                float r = channelCount > 1 ? interleaved[baseIdx + 1] : l;
                if (matrix != null)
                {
                    l = r = 0;
                    for (int channel = 0; channel < channelCount; channel++)
                    {
                        float leftGain = matrix.GetCoefficient(0, channel), rightGain = matrix.GetCoefficient(1, channel);
                        if (leftGain != 0) l += interleaved[baseIdx + channel] * leftGain;
                        if (rightGain != 0) r += interleaved[baseIdx + channel] * rightGain;
                        _channels[channel][idx] = interleaved[baseIdx + channel];
                    }
                }
                _left[idx] = l;
                _right[idx] = r;
                idx++;
                if (idx >= capacity) idx = 0;
            }
            _writeIndex = idx;
            _totalWritten += frames;

            _hasAnchor = true;
            _lastWriteEndIndex = _totalWritten;
            _lastWriteEndTime = startTime + MediaTime.FromSamples(frames, sampleRate);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            Array.Clear(_left);
            Array.Clear(_right);
            foreach (float[] channel in _channels) Array.Clear(channel);
            _writeIndex = 0;
            _totalWritten = 0;
            _hasAnchor = false;
            _lastWriteEndIndex = 0;
            _lastWriteEndTime = MediaTime.Zero;
        }
    }

    // Reads `length` samples that end exactly at the sample corresponding to
    // `playheadTime` in scene time. Missing samples before/after the available
    // window are zero-padded. Returns the number of real (non-padded) samples,
    // and reports the offset of those real samples within the destination span
    // via `leadingZeros` so callers doing analysis (RMS, LUFS, ...) can skip
    // the silent prefix.
    public int ReadAroundTime(MediaTime playheadTime, Span<float> destLeft, Span<float> destRight, int length)
        => ReadAroundTime(playheadTime, destLeft, destRight, length, out _);

    public int ReadAroundTime(MediaTime playheadTime, Span<float> destLeft, Span<float> destRight, int length, out int leadingZeros)
    {
        leadingZeros = 0;
        if (destLeft.Length < length || destRight.Length < length) return 0;
        lock (_gate)
        {
            if (!_hasAnchor || _sampleRate <= 0)
            {
                destLeft.Slice(0, length).Clear();
                destRight.Slice(0, length).Clear();
                return 0;
            }

            int got = LocateLocked(playheadTime, length, out leadingZeros, out int ringStart);
            CopyLocked(_left, destLeft, length, leadingZeros, got, ringStart);
            CopyLocked(_right, destRight, length, leadingZeros, got, ringStart);
            return got;
        }
    }

    // Per-channel variant of ReadAroundTime. `dest` needs ChannelCount arrays of at least
    // `length` samples; channels are unfolded (a mono source yields one channel).
    // Returns 0 without touching `dest` when its shape doesn't match.
    public int ReadChannelsAroundTime(MediaTime playheadTime, float[][] dest, int length, out int leadingZeros)
    {
        leadingZeros = 0;
        lock (_gate)
        {
            if (dest.Length != _sourceChannels.Length) return 0;
            foreach (float[] channel in dest)
            {
                if (channel.Length < length) return 0;
            }

            int got = 0;
            int ringStart = 0;
            if (_hasAnchor && _sampleRate > 0)
            {
                got = LocateLocked(playheadTime, length, out leadingZeros, out ringStart);
            }

            for (int ch = 0; ch < dest.Length; ch++)
            {
                CopyLocked(_sourceChannels[ch], dest[ch], length, leadingZeros, got, ringStart);
            }

            return got;
        }
    }

    // Snapshot of `windowSamples` ending at `playheadTime` used for RMS / peak
    // calculations.
    public (float LeftRms, float RightRms, float LeftPeak, float RightPeak) ComputeMeters(
        int windowSamples, MediaTime playheadTime)
    {
        Span<float> l = windowSamples <= 4096 ? stackalloc float[windowSamples] : new float[windowSamples];
        Span<float> r = windowSamples <= 4096 ? stackalloc float[windowSamples] : new float[windowSamples];
        int got = ReadAroundTime(playheadTime, l, r, windowSamples, out int leadingZeros);
        if (got <= 0) return (0, 0, 0, 0);

        // Real samples live at [leadingZeros, leadingZeros + got). Slicing from 0
        // would analyze the silent prefix and drop samples off the end.
        ReadOnlySpan<float> lSlice = l.Slice(leadingZeros, got);
        ReadOnlySpan<float> rSlice = r.Slice(leadingZeros, got);
        return (
            AudioMath.CalculateRms(lSlice),
            AudioMath.CalculateRms(rSlice),
            AudioMath.FindPeak(lSlice),
            AudioMath.FindPeak(rSlice));
    }

    // Per-channel RMS / peak over `windowSamples` ending at `playheadTime`. `samples` is
    // caller-owned scratch shaped as ReadChannelsAroundTime expects. Returns the channel
    // count written to `rms` / `peak`, or 0 when nothing is buffered.
    public int ComputeChannelMeters(
        int windowSamples, MediaTime playheadTime, float[][] samples, Span<float> rms, Span<float> peak)
    {
        int channels = samples.Length;
        if (rms.Length < channels || peak.Length < channels) return 0;
        int got = ReadChannelsAroundTime(playheadTime, samples, windowSamples, out int leadingZeros);
        if (got <= 0) return 0;

        for (int ch = 0; ch < channels; ch++)
        {
            ReadOnlySpan<float> slice = samples[ch].AsSpan(leadingZeros, got);
            rms[ch] = AudioMath.CalculateRms(slice);
            peak[ch] = AudioMath.FindPeak(slice);
        }

        return channels;
    }

    // Maps `playheadTime` to a ring range: the real samples land at
    // [leadingZeros, leadingZeros + returned count) of a `length` window and start at
    // ring index `ringStart`. Must be called with an anchor.
    private int LocateLocked(MediaTime playheadTime, int length, out int leadingZeros, out int ringStart)
    {
        leadingZeros = 0;
        ringStart = 0;
        if (_capacity == 0) return 0;

        double secondsFromEnd = (_lastWriteEndTime - playheadTime).ToSeconds();
        long samplesFromEnd = (long)Math.Round(secondsFromEnd * _sampleRate);
        long endAbsIndex = _lastWriteEndIndex - samplesFromEnd;

        long oldestAbs = Math.Max(0L, _totalWritten - _capacity);
        if (endAbsIndex > _totalWritten) endAbsIndex = _totalWritten;
        if (endAbsIndex <= oldestAbs) return 0;

        long startAbs = endAbsIndex - length;
        if (startAbs < oldestAbs)
        {
            leadingZeros = (int)(oldestAbs - startAbs);
            startAbs = oldestAbs;
        }

        int toCopy = (int)(endAbsIndex - startAbs);
        if (toCopy <= 0) return 0;

        long backFromWriteIdx = _totalWritten - startAbs;
        ringStart = (int)(((long)_writeIndex - backFromWriteIdx) % _capacity);
        if (ringStart < 0) ringStart += _capacity;
        return toCopy;
    }

    // Copies `toCopy` ring samples from `ringStart` into dest[leadingZeros..] and
    // zero-pads the rest of the `length` window.
    private void CopyLocked(float[] source, Span<float> dest, int length, int leadingZeros, int toCopy, int ringStart)
    {
        if (toCopy <= 0)
        {
            dest.Slice(0, length).Clear();
            return;
        }

        dest.Slice(0, leadingZeros).Clear();
        int firstChunk = Math.Min(toCopy, _capacity - ringStart);
        source.AsSpan(ringStart, firstChunk).CopyTo(dest.Slice(leadingZeros));

        int remaining = toCopy - firstChunk;
        if (remaining > 0)
        {
            source.AsSpan(0, remaining).CopyTo(dest.Slice(leadingZeros + firstChunk));
        }

        // Trailing zero-pad if the request extends past the anchor end.
        int trailingStart = leadingZeros + toCopy;
        dest.Slice(trailingStart, length - trailingStart).Clear();
    }

    private void ResetInternal(int sampleRate, int capacity, int channelCount)
    {
        _sampleRate = sampleRate;
        _capacity = capacity;
        _channelCount = channelCount;
        _left = new float[capacity];
        _right = new float[capacity];
        _channels = new float[channelCount > 2 ? channelCount : 0][];
        for (int ch = 0; ch < _channels.Length; ch++) _channels[ch] = new float[capacity];
        _sourceChannels = channelCount switch
        {
            1 => [_left],
            2 => [_left, _right],
            _ => _channels,
        };
        _writeIndex = 0;
        _totalWritten = 0;
        _hasAnchor = false;
        _lastWriteEndIndex = 0;
        _lastWriteEndTime = MediaTime.Zero;
    }
}
