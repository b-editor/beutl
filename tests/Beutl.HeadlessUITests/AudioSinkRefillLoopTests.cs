using Beutl.Audio;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class AudioSinkRefillLoopTests
{
    private const int SampleRate = 48000;

    [Test]
    public async Task Primes_every_buffer_then_refills_each_completed_buffer_and_drains_at_the_scene_end()
    {
        var sink = new FakeAudioSink(bufferCount: 2);
        var composed = new List<double>();
        var published = new List<double>();
        AudioSinkRefillLoop loop = CreateLoop(sink, composed, sceneEndSeconds: 4,
            onPoll: _ => sink.CompleteBuffers(1), published: published);

        await loop.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(composed, Is.EqualTo(new double[] { 0, 1, 2, 3 }),
                "each buffer composes the second after the previous one, and refilling stops at the scene end");
            Assert.That(published, Is.EqualTo(composed), "every queued buffer is published with its start time");
            Assert.That(sink.Events.Take(3), Is.EqualTo(new[] { "queue", "queue", "play" }),
                "every buffer is queued before playback starts");
            Assert.That(sink.Events.Count(e => e == "requeue"), Is.EqualTo(2),
                "a buffer is refilled only after the sink reports it as processed");
            Assert.That(sink.QueuedCount, Is.Zero, "the queued audio plays out before the loop stops");
            Assert.That(sink.Events[^1], Is.EqualTo("stop"));
        });
    }

    [Test]
    public async Task Refills_every_buffer_processed_since_the_last_poll_and_restarts_a_starved_sink()
    {
        var sink = new FakeAudioSink(bufferCount: 2);
        var composed = new List<double>();
        // The first poll finds both buffers played: the device ran dry and stopped.
        AudioSinkRefillLoop loop = CreateLoop(sink, composed, sceneEndSeconds: 6,
            onPoll: poll => sink.CompleteBuffers(poll == 0 ? 2 : 1));

        await loop.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(composed, Is.EqualTo(new double[] { 0, 1, 2, 3, 4, 5 }));
            Assert.That(sink.Events.Count(e => e == "play"), Is.EqualTo(2),
                "a sink that ran out of buffers is started again once refilled");
            Assert.That(sink.MaxQueued, Is.LessThanOrEqualTo(2));
        });
    }

    [Test]
    public async Task Stops_without_draining_or_composing_once_playback_is_paused()
    {
        var sink = new FakeAudioSink(bufferCount: 2);
        var composed = new List<double>();
        bool playing = true;
        AudioSinkRefillLoop loop = CreateLoop(sink, composed, sceneEndSeconds: 60,
            onPoll: poll =>
            {
                sink.CompleteBuffers(1);
                if (poll == 2) playing = false;
            },
            isPlaying: () => playing);

        await loop.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(composed, Is.EqualTo(new double[] { 0, 1, 2, 3 }), "nothing is composed after the pause");
            Assert.That(sink.QueuedCount, Is.GreaterThan(0), "a pause does not wait for the queued audio");
            Assert.That(sink.Events[^1], Is.EqualTo("stop"));
        });
    }

    [Test]
    public async Task Cancellation_for_a_seek_stops_the_sink_and_ends_without_throwing()
    {
        var sink = new FakeAudioSink(bufferCount: 2);
        var composed = new List<double>();
        using var cts = new CancellationTokenSource();
        AudioSinkRefillLoop loop = CreateLoop(sink, composed, sceneEndSeconds: 60,
            onPoll: poll =>
            {
                sink.CompleteBuffers(1);
                if (poll == 1) cts.Cancel();
            });

        await loop.RunAsync(cts.Token).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(composed, Is.EqualTo(new double[] { 0, 1, 2 }), "nothing is composed after the cancellation");
            Assert.That(sink.Events[^1], Is.EqualTo("stop"));
        });
    }

    [Test]
    public async Task Anchors_the_clock_to_the_samples_the_sink_played()
    {
        var sink = new FakeAudioSink(bufferCount: 2);
        var clock = new AudioPlaybackClock();
        var anchored = new List<double>();
        AudioSinkRefillLoop loop = CreateLoop(sink, [], sceneEndSeconds: 4,
            onPoll: _ =>
            {
                anchored.Add(clock.GetTime()!.Value.ToSeconds());
                sink.CompleteBuffers(1);
            },
            clock: clock, startSeconds: 10);

        await loop.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(clock.StartedTask.IsCompleted, Is.True);
        // The fake reports one sample into the playing buffer, then whole seconds as buffers complete.
        Assert.That(anchored, Has.Count.EqualTo(4));
        for (int i = 0; i < anchored.Count; i++)
        {
            Assert.That(anchored[i], Is.EqualTo(10 + i).Within(0.25), $"poll {i}");
        }
    }

    [Test]
    public async Task Gives_up_on_a_sink_that_stops_playing_mid_scene()
    {
        var sink = new FakeAudioSink(bufferCount: 2);
        var composed = new List<double>();
        // The device plays the first buffer, then stops making progress (unplugged or suspended).
        AudioSinkRefillLoop loop = CreateLoop(sink, composed, sceneEndSeconds: 60,
            onPoll: poll => sink.CompleteBuffers(poll == 0 ? 1 : 0));

        await loop.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(composed, Is.EqualTo(new double[] { 0, 1, 2 }), "nothing more is composed for a stalled sink");
            Assert.That(sink.Events[^1], Is.EqualTo("stop"), "the loop ends before the scene end and stops the sink");
        });
    }

    private static AudioSinkRefillLoop CreateLoop(
        FakeAudioSink sink, List<double> composed, double sceneEndSeconds, Action<int> onPoll,
        List<double>? published = null, Func<bool>? isPlaying = null,
        AudioPlaybackClock? clock = null, double startSeconds = 0)
    {
        MediaTime sceneEnd = MediaTime.FromSeconds(startSeconds + sceneEndSeconds);
        int poll = 0;
        return new AudioSinkRefillLoop
        {
            Sink = sink,
            Clock = clock ?? new AudioPlaybackClock(),
            StartTime = MediaTime.FromSeconds(startSeconds),
            SampleRate = SampleRate,
            Compose = time =>
            {
                composed.Add(time.ToSeconds() - startSeconds);
                return (new AudioBuffer(SampleRate, 2, SampleRate), sceneEnd);
            },
            Publish = (_, start) => published?.Add(start.ToSeconds() - startSeconds),
            IsPlaying = isPlaying ?? (() => true),
            Delay = (_, token) =>
            {
                token.ThrowIfCancellationRequested();
                // A loop that stops making progress fails here instead of spinning forever.
                if (poll >= 100) throw new InvalidOperationException("The loop kept polling without finishing.");
                onPoll(poll++);
                return Task.CompletedTask;
            },
        };
    }

    // Plays buffers only when the test completes them. Like OpenAL, it stops once nothing is queued.
    private sealed class FakeAudioSink(int bufferCount) : IAudioSink
    {
        private readonly Queue<int> _queued = new();
        private readonly Queue<int> _processed = new();
        private int _unqueued = bufferCount;
        private long _completedSamples;
        private bool _playing;

        public List<string> Events { get; } = [];

        public int QueuedCount => _queued.Count;

        public int MaxQueued { get; private set; }

        public int BufferCount { get; } = bufferCount;

        // One sample into the playing buffer, so the startup wait sees progress.
        public long PlayedSamples => _completedSamples + (_playing && _queued.Count > 0 ? 1 : 0);

        public int ProcessedBufferCount => _processed.Count;

        public bool IsPlaying => _playing;

        public void CompleteBuffers(int count)
        {
            for (int i = 0; i < count && _playing && _queued.Count > 0; i++)
            {
                int samples = _queued.Dequeue();
                _completedSamples += samples;
                _processed.Enqueue(samples);
            }

            if (_queued.Count == 0) _playing = false;
        }

        public void QueueBuffer(AudioBuffer? audio)
        {
            if (_unqueued == 0) throw new InvalidOperationException("Every buffer is already queued.");
            _unqueued--;
            Enqueue(audio);
            Events.Add("queue");
        }

        public void RequeueProcessedBuffer(AudioBuffer? audio)
        {
            if (_processed.Count == 0) throw new InvalidOperationException("No buffer has finished playing.");
            _processed.Dequeue();
            Enqueue(audio);
            Events.Add("requeue");
        }

        public void ReleaseProcessedBuffers()
        {
            _processed.Clear();
        }

        public void Play()
        {
            _playing = _queued.Count > 0;
            Events.Add("play");
        }

        public void Stop()
        {
            _playing = false;
            Events.Add("stop");
        }

        private void Enqueue(AudioBuffer? audio)
        {
            _queued.Enqueue(audio?.SampleCount ?? 0);
            MaxQueued = Math.Max(MaxQueued, _queued.Count + _processed.Count);
        }
    }
}
