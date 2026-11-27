using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Composition;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl;

public sealed class SceneComposer : Composer
{
    private readonly SceneCompositor _compositor;
    private MediaTime? _nextOutputTime;
    private MediaTime _nextInputTime;
    private MediaTime _inputBlockDuration;
    private MediaTime _playbackEnd;
    private int _playbackLatency;
    private int _playbackVersion;
    private int? _playbackGeneration;
    private CompositionEligibility? _drainEligibility;
    private bool _hasComposed;
    private AudioBuffer? _inputBuffer;
    private int _inputPosition;
    private int _leadingSamples;
    private bool _disposing;

    public SceneComposer(
        Scene scene,
        SceneRenderServices services,
        bool disableResourceShare = false,
        bool forceOriginalSource = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        _compositor = new SceneCompositor(scene, services)
        {
            DisableResourceShare = disableResourceShare,
            ForceOriginalSource = forceOriginalSource,
        };
    }

    public SceneCompositor Compositor => _compositor;

    public AudioBuffer? Compose(TimeRange timeRange)
    {
        if (_playbackLatency > 0)
            InvalidateCache();
        DiscardInputBuffer();
        _nextOutputTime = null;
        _playbackLatency = 0;
        return ComposeRaw(timeRange, playbackLatency: null);
    }

    internal AudioBuffer? ComposePlayback(TimeRange range, MediaTime sceneEnd, int? generation = null)
    {
        if (range.Duration == MediaTime.Zero)
            return new AudioBuffer(SampleRate, ChannelLayout.ChannelCount, 0);

        int version = _compositor.AudioEligibilityVersion;
        bool continuous = _nextOutputTime is { } next && IsContiguous(next, range.Start)
            && _playbackEnd == sceneEnd && _playbackVersion == version && _playbackGeneration == generation;
        // Advance the output cursor only on success, so a failed read restarts the sequence.
        _nextOutputTime = null;
        if (!continuous)
        {
            DiscardInputBuffer();
            if (_playbackLatency > 0)
            {
                InvalidateCache();
                _hasComposed = false;
            }
            _nextInputTime = range.Start;
            _inputBlockDuration = range.Duration;
            _playbackEnd = sceneEnd;
            _playbackVersion = version;
            _playbackGeneration = generation;
            _drainEligibility = null;
            _playbackLatency = range.Start < sceneEnd
                ? GetPlaybackLatency(_compositor.EvaluateAudio(TimeRange.FromRange(range.Start, sceneEnd)))
                : 0;

            // An output seek can land at the previous input cursor. Starting with fresh
            // graphs also drops any delay history in that case.
            if (_playbackLatency > 0 && _hasComposed)
                InvalidateCache();
            _leadingSamples = _playbackLatency;
        }

        AudioBuffer? result = _playbackLatency > 0 ? ReadAligned(range) : ComposeRaw(range, playbackLatency: 0);
        if (result != null)
            _nextOutputTime = range.End;
        return result;
    }

    private AudioBuffer? ReadAligned(TimeRange range)
    {
        int sampleCount = AudioProcessContext.GetSampleCount(range, SampleRate);
        var output = new AudioBuffer(SampleRate, ChannelLayout.ChannelCount, sampleCount);
        AudioBuffer? input = Interlocked.Exchange(ref _inputBuffer, null);
        int position = _inputPosition;
        try
        {
            for (int written = 0; written < sampleCount;)
            {
                if (input == null)
                {
                    input = ReadInputBlock(Math.Max(_leadingSamples, sampleCount - written));
                    if (input == null)
                    {
                        output.Dispose();
                        return null;
                    }
                    if (input.SampleCount == 0)
                        throw new InvalidOperationException("Audio composition returned an empty input block.");
                    position = 0;
                }

                int skip = Math.Min(_leadingSamples, input.SampleCount - position);
                position += skip;
                _leadingSamples -= skip;
                int count = Math.Min(sampleCount - written, input.SampleCount - position);
                for (int ch = 0; ch < output.ChannelCount; ch++)
                {
                    input.GetChannelData(ch).Slice(position, count).CopyTo(output.GetChannelData(ch)[written..]);
                }

                position += count;
                written += count;
                if (position == input.SampleCount)
                {
                    input.Dispose();
                    input = null;
                }
            }

            _inputPosition = position;
            Volatile.Write(ref _inputBuffer, input);
            input = null;
            // The editor can replace and dispose its composer while a read is in progress.
            if (Volatile.Read(ref _disposing))
                DiscardInputBuffer();
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
        finally
        {
            input?.Dispose();
        }
    }

    private AudioBuffer? ReadInputBlock(int drainSamples)
    {
        // Keep the input block boundaries intact and skip the delay in samples, so the
        // effects stay contiguous and the source's sample-time rounding is unchanged.
        if (_nextInputTime < _playbackEnd)
        {
            MediaTime remaining = _playbackEnd - _nextInputTime;
            MediaTime duration = remaining < _inputBlockDuration ? remaining : _inputBlockDuration;
            return ComposeRaw(new TimeRange(_nextInputTime, duration), _playbackLatency);
        }

        var tailRange = new TimeRange(_nextInputTime, AudioProcessContext.GetDurationForSampleCount(drainSamples, SampleRate));
        AudioBuffer? result = Flush(tailRange, _drainEligibility);
        _nextInputTime = tailRange.End;
        return result;
    }

    private void DiscardInputBuffer() => Interlocked.Exchange(ref _inputBuffer, null)?.Dispose();

    private AudioBuffer? ComposeRaw(TimeRange timeRange, int? playbackLatency)
    {
        MasterGain = _compositor.Scene.MasterGain;
        var frame = _compositor.EvaluateAudio(timeRange);
        _hasComposed = true;
        AudioBuffer? result = base.Compose(timeRange, frame, playbackLatency);
        _drainEligibility = frame.Eligibility;
        _nextInputTime = timeRange.End;
        return result;
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            Volatile.Write(ref _disposing, true);
            DiscardInputBuffer();
        }
        base.OnDispose(disposing);
        if (disposing)
            _compositor.Dispose();
    }
}
