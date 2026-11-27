using Beutl.Audio;
using Beutl.Media;
using Beutl.Models;

namespace Beutl.ViewModels;

// The audio queue and the video producer use the same sequence of passes. Output samples and
// frame positions advance monotonically even when the source position jumps back at a wrap.
internal sealed class LoopPlayback
{
    public static readonly MediaTime BufferDuration = MediaTime.FromSeconds(0.1);
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);
    private static readonly MediaTime s_audioLookAhead = BufferDuration + BufferDuration;

    private readonly Rational _rate;
    private readonly int _sampleRate;
    private readonly Func<PlaybackRange?> _nextRange;
    private readonly List<Pass> _passes = [];
    private bool _ended;
    private bool _reachedNaturalEnd;

    public LoopPlayback(PlaybackRange first, Rational rate, int sampleRate, Func<PlaybackRange?> nextRange,
        MediaTime? firstTime = null)
    {
        _rate = rate;
        _sampleRate = sampleRate;
        _nextRange = nextRange;
        _passes.Add(CreatePass(first, 0, 0, 0, firstTime));
    }

    public event Action? PassAdded;

    internal int RetainedPassCount
    {
        get
        {
            lock (_passes)
                return _passes.Count;
        }
    }

    public int? EndPosition
    {
        get
        {
            lock (_passes)
                return _ended ? _passes[^1].EndPosition : null;
        }
    }

    public MediaTime? EndTime
    {
        get
        {
            lock (_passes)
                return _ended ? MediaTime.FromSamples(_passes[^1].EndSample, _sampleRate) : null;
        }
    }

    public bool ReachedNaturalEnd
    {
        get
        {
            lock (_passes)
                return _reachedNaturalEnd;
        }
    }

    public void FinishAudio()
    {
        lock (_passes)
            _ended = true;
    }

    public bool TryGetPass(int position, out Pass pass)
    {
        lock (_passes)
        {
            for (int i = _passes.Count - 1; i >= 0; i--)
            {
                pass = _passes[i];
                if (pass.Position <= position) return position < pass.EndPosition;
            }
        }

        pass = default;
        return false;
    }

    private void DiscardPlayedPasses(long sample)
    {
        lock (_passes)
        {
            // Keep the pass covering the oldest possible audio position and its predecessor.
            int remove = 0;
            while (remove + 1 < _passes.Count && _passes[remove + 1].EndSample <= sample)
                remove++;
            _passes.RemoveRange(0, remove);
        }
    }

    public int GetPosition(MediaTime elapsed) => GetPosition(elapsed, out _);

    public int GetPosition(MediaTime elapsed, out Pass pass)
    {
        long sample = ToSample(elapsed);
        pass = FindBySample(sample);
        if (sample >= pass.EndSample) return pass.EndPosition;
        long sourceSample = pass.SourceSample + Math.Max(0, sample - pass.Sample);
        MediaTime time = MediaTime.FromSamples(sourceSample, _sampleRate);
        int frame = (int)time.ToFrame(_rate, MidpointRounding.ToNegativeInfinity);
        if (frame < pass.Range.EndFrame - 1 && FrameSample(frame + 1) <= sourceSample) frame++;
        return pass.Position + Math.Clamp(frame - pass.Range.StartFrame, 0, pass.EndPosition - pass.Position - 1);
    }

    public MediaTime GetSceneTime(MediaTime elapsed)
    {
        long sample = ToSample(elapsed);
        Pass pass = FindBySample(sample);
        long offset = Math.Clamp(sample - pass.Sample, 0, pass.EndSample - pass.Sample - 1);
        return MediaTime.FromSamples(pass.SourceSample + offset, _sampleRate);
    }

    public MediaTime GetOutputTime(Pass pass, int frame)
        => MediaTime.FromSamples(pass.Sample + Math.Max(0, FrameSample(frame) - pass.SourceSample), _sampleRate);

    public AudioBuffer ComposeAudio(MediaTime elapsed, int channels, Func<TimeRange, AudioBuffer?> compose)
    {
        // The sinks queue at most two buffers ahead of playback. Trim by audio progress so a
        // blocked renderer or UI cannot keep passes alive, and keep the whole buffer being filled.
        DiscardPlayedPasses(ToSample(elapsed - s_audioLookAhead));
        long start = ToSample(elapsed);
        int count = (int)(ToSample(elapsed + BufferDuration) - start);
        var buffer = new AudioBuffer(_sampleRate, channels, count);
        try
        {
            int offset = 0;
            while (offset < count)
            {
                if (!TryGetAudioSlice(start + offset, count - offset, out TimeRange range, out int length))
                {
                    if (!PlanNextPass(start + offset)) break;
                    continue;
                }

                // Compose each contiguous span separately, with the cut at the first sample at or
                // after the exclusive end. Queue the two sides of the cut in a single native stream.
                using AudioBuffer? source = compose(range);
                source?.CopyTo(buffer, offset, Math.Min(length, source.SampleCount));
                offset += length;
            }

            return buffer;
        }
        catch
        {
            buffer.Dispose();
            throw;
        }
    }

    public bool TryGetAudioSlice(long sample, int count, out TimeRange range, out int length)
    {
        Pass pass = FindBySample(sample);
        if (sample < pass.Sample || sample >= pass.EndSample)
        {
            range = default;
            length = 0;
            return false;
        }

        length = (int)Math.Min(count, pass.EndSample - sample);
        range = new TimeRange(CompositionTime(pass.SourceSample + sample - pass.Sample), CompositionTime(length));
        return true;
    }

    private bool PlanNextPass(long sample)
    {
        lock (_passes)
        {
            if (_ended || sample != _passes[^1].EndSample) return false;
        }

        // Read the selection only as the audio queue reaches a cut. Once queued, the picture
        // must follow the same decision as the samples committed to the audio device.
        PlaybackRange? next = _nextRange();
        lock (_passes)
        {
            if (next is not { } range)
            {
                _ended = true;
                _reachedNaturalEnd = true;
                return false;
            }

            Pass previous = _passes[^1];
            _passes.Add(CreatePass(range, previous.EndPosition, previous.EndSample, previous.Position));
        }

        PassAdded?.Invoke();
        return true;
    }

    private Pass FindBySample(long sample)
    {
        lock (_passes)
        {
            for (int i = _passes.Count - 1; i > 0; i--)
            {
                if (_passes[i].Sample <= sample) return _passes[i];
            }

            return _passes[0];
        }
    }

    private Pass CreatePass(PlaybackRange range, int position, long sample, int previousPosition, MediaTime? firstTime = null)
    {
        long last = FrameSample(range.EndFrame);
        long first = firstTime is { } time
            ? Math.Min(last - 1, time.ToSamples(_sampleRate, MidpointRounding.ToPositiveInfinity))
            : FrameSample(range.StartFrame);
        return new Pass(range, position, sample, first, sample + last - first, previousPosition);
    }

    private long FrameSample(int frame)
        => MediaTime.FromFrames(frame, _rate).ToSamples(_sampleRate, MidpointRounding.ToPositiveInfinity);

    // Some sample rates cannot represent every exact sample time in flicks. Keep conversion
    // back to a source sample index (which truncates) on the intended side of the boundary.
    private MediaTime CompositionTime(long sample)
    {
        MediaTime time = MediaTime.FromSamples(sample, _sampleRate);
        return time.ToSamples(_sampleRate, MidpointRounding.ToZero) == sample
            ? time
            : new MediaTime(time.Flicks + Math.Sign(sample));
    }

    private long ToSample(MediaTime time) => time.ToSamples(_sampleRate, MidpointRounding.AwayFromZero);

    internal readonly record struct Pass(
        PlaybackRange Range, int Position, long Sample, long SourceSample, long EndSample, int PreviousPosition)
    {
        public int EndPosition => Position + Range.EndFrame - Range.StartFrame;

        public int GetFrame(int position) => Range.StartFrame + position - Position;
    }
}
