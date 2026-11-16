using Beutl.Audio;

namespace Beutl.ViewModels;

// The refill loop shared by the XAudio2 and OpenAL sinks: queue every buffer, start playback,
// then poll the sink and refill each buffer it finished, anchoring the playback clock to the
// sink's position on every poll.
internal sealed class AudioSinkRefillLoop
{
    internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private static readonly MediaTime s_bufferDuration = MediaTime.FromSeconds(1);

    public required IAudioSink Sink { get; init; }

    public required AudioPlaybackClock Clock { get; init; }

    public required MediaTime StartTime { get; init; }

    // The rate used for the clock until the first composed buffer reports its own.
    public required int SampleRate { get; init; }

    // Composes one second of audio starting at the given time, and reports where the scene ends.
    public required Func<MediaTime, (AudioBuffer? Audio, MediaTime SceneEnd)> Compose { get; init; }

    // Called with every composed buffer and its start time after it is queued.
    public required Action<AudioBuffer, MediaTime> Publish { get; init; }

    public required Func<bool> IsPlaying { get; init; }

    // Called when the sink has audio but did not advance within the startup deadline.
    public Action? OnStartupStalled { get; init; }

    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;

    public async Task RunAsync(CancellationToken token)
    {
        MediaTime cur = StartTime;
        MediaTime sceneEndTime = default;
        int sampleRate = SampleRate;
        bool hasAudio = false;
        bool audioClockValid = false;

        void AnchorClock()
        {
            if (!hasAudio || !audioClockValid) return;
            double seconds = (double)Sink.PlayedSamples / sampleRate;
            Clock.Anchor(StartTime + MediaTime.FromSeconds(seconds));
        }

        // Composes the next second and queues it, in a new buffer or in the oldest processed one.
        void FillAndQueue(bool reuseProcessedBuffer)
        {
            token.ThrowIfCancellationRequested();
            MediaTime bufferStartTime = cur;
            var bufferData = Compose(cur);
            sceneEndTime = bufferData.SceneEnd;
            using AudioBuffer? audio = bufferData.Audio;
            token.ThrowIfCancellationRequested();
            cur += s_bufferDuration;
            if (audio != null && !hasAudio)
            {
                sampleRate = audio.SampleRate;
            }

            if (reuseProcessedBuffer)
                Sink.RequeueProcessedBuffer(audio);
            else
                Sink.QueueBuffer(audio);

            if (audio != null)
            {
                hasAudio = true;
                Publish(audio, bufferStartTime);
            }
        }

        try
        {
            for (int i = 0; i < Sink.BufferCount; i++)
            {
                FillAndQueue(reuseProcessedBuffer: false);
            }

            token.ThrowIfCancellationRequested();
            Sink.Play();

            // Anchor only once the first sample is out (see WaitForFirstSampleAsync).
            audioClockValid = await WaitForFirstSampleAsync(
                    () => Sink.PlayedSamples > 0, hasAudio, token)
                .ConfigureAwait(false);
            if (hasAudio && !audioClockValid)
            {
                OnStartupStalled?.Invoke();
            }
            AnchorClock();
            Clock.SignalStarted();

            while (IsPlaying() && !token.IsCancellationRequested)
            {
                int processed = Sink.ProcessedBufferCount;
                while (processed > 0)
                {
                    FillAndQueue(reuseProcessedBuffer: true);
                    processed--;
                }

                // A device that ran out of buffers stops; start it again once it has audio.
                if (!Sink.IsPlaying)
                {
                    Sink.Play();
                }

                AnchorClock();

                await Delay(PollInterval, token).ConfigureAwait(false);
                if (cur >= sceneEndTime)
                    break;
            }

            // The final buffer is silence-padded past the scene end; let the queued audio play out.
            while (Sink.IsPlaying && IsPlaying() && !token.IsCancellationRequested)
            {
                Sink.ReleaseProcessedBuffers();
                AnchorClock();
                await Delay(PollInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            Sink.Stop();
        }
    }

    // Waits with a short poll until the backend outputs its first sample. Anchoring before that lets
    // the video run ahead and then jump back at the next anchor. Returns true when the caller can
    // anchor. Returns false when no progress is seen before the deadline or there is no audio; the
    // caller then gives up on the audio clock and uses the wall clock (so a suspended or disconnected
    // device cannot hang playback). AnchorClock does nothing in that case, so the caller signals the
    // start with SignalStarted.
    internal static async Task<bool> WaitForFirstSampleAsync(
        Func<bool> hasProgressed, bool hasAudio, CancellationToken token)
    {
        if (!hasAudio) return false;
        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2; // 2s
        while (!token.IsCancellationRequested)
        {
            if (hasProgressed()) return true;
            if (Stopwatch.GetTimestamp() >= deadline) return false;
            try
            {
                await Task.Delay(1, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        return false;
    }
}
