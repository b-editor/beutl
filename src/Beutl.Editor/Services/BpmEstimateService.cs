using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Graph;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

internal readonly record struct BpmEstimate(
    double Bpm,
    double FirstBeatOffsetSeconds,
    double Confidence);

// Estimates a clip's tempo from the audio its used range actually plays, so trims, speed and effects count.
internal static class BpmEstimateService
{
    // A minute holds enough beats at any supported tempo; longer recordings would only grow the PCM buffer.
    internal const double MaxAnalysisSeconds = 60;

    internal static bool CanAnalyze(Element element) => SilenceWaveform.HasAnalyzableAudio(element);

    internal static async Task<BpmEstimate?> AnalyzeAsync(
        Scene scene,
        Element element,
        FontManager fonts,
        DecoderRegistry? decoders,
        CancellationToken cancellationToken,
        IProgress<int>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanAnalyze(element))
        {
            return null;
        }

        int sampleRate = scene.GetSampleRate();
        double durationSeconds = Math.Min(element.Length.Value.ToSeconds(), MaxAnalysisSeconds);
        int totalSamples = checked((int)Math.Round(durationSeconds * sampleRate));
        if (sampleRate <= 0 || totalSamples <= 0)
        {
            return null;
        }

        // Composition reads the live scene's graph, so it stays on the thread that owns the scene while only the
        // analysis itself moves to the pool.
        TaskScheduler owner = SynchronizationContext.Current is null
            ? TaskScheduler.Current
            : TaskScheduler.FromCurrentSynchronizationContext();
        var samples = new float[totalSamples];
        var services = new SceneRenderServices(fonts, decoders);
        var composer = new Composer { SampleRate = sampleRate, ChannelLayout = scene.GetChannelLayout() };
        var compositor = new SceneCompositor(scene, services) { ForceOriginalSource = true };
        try
        {
            int reported = -1;
            for (int written = 0; written < totalSamples;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int count = Math.Min(sampleRate, totalSamples - written);
                int destination = written;
                var window = new TimeRange(
                    element.Start.Value + MediaTime.FromSamples(written, sampleRate),
                    AudioProcessContext.GetDurationForSampleCount(count, sampleRate));

                await Task.Factory.StartNew(
                    () => ComposeWindow(window, count, destination),
                    cancellationToken,
                    TaskCreationOptions.DenyChildAttach,
                    owner).ConfigureAwait(false);

                written += count;
                int percent = (int)((long)written * 100 / totalSamples);
                if (percent != reported)
                {
                    reported = percent;
                    progress?.Report(percent);
                }
            }

            var analyzer = new AudioRhythmAnalyzer(decoders);
            AudioRhythmAnalysis analysis = await Task.Run(
                () => analyzer.AnalyzePcm(samples, sampleRate),
                cancellationToken).ConfigureAwait(false);
            if (!(analysis.EstimatedBpm > 0) || analysis.BeatTimesSeconds.Count == 0)
            {
                return null;
            }

            // The grid repeats, so only the first beat's phase within a beat matters.
            double bpm = Math.Round(analysis.EstimatedBpm, 1, MidpointRounding.AwayFromZero);
            double firstBeatSceneSeconds = element.Start.Value.ToSeconds() + analysis.BeatTimesSeconds[0];
            double offset = MapOffsetSeconds(firstBeatSceneSeconds, 60d / bpm);
            return new BpmEstimate(bpm, offset, analysis.Confidence);
        }
        finally
        {
            await Task.Factory.StartNew(
                () =>
                {
                    try
                    {
                        composer.Dispose();
                    }
                    finally
                    {
                        compositor.Dispose();
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                owner).ConfigureAwait(false);
        }

        void ComposeWindow(TimeRange window, int count, int destination)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reads = new AudioReadScope();
            using AudioBuffer? buffer = composer.Compose(window, compositor.EvaluateElementAudio(element, window));
            if (reads.HasFailures)
            {
                throw new InvalidDataException("Audio could not be read.");
            }

            if (buffer is { SampleCount: > 0, ChannelCount: > 0 })
            {
                // Mix every channel: after pan and routing, one channel alone can be silent.
                int copied = Math.Min(count, buffer.SampleCount);
                Span<float> target = samples.AsSpan(destination, copied);
                float scale = 1f / buffer.ChannelCount;
                for (int channel = 0; channel < buffer.ChannelCount; channel++)
                {
                    Span<float> source = buffer.GetChannelData(channel)[..copied];
                    for (int i = 0; i < copied; i++)
                    {
                        target[i] += source[i] * scale;
                    }
                }
            }
        }
    }

    // Where a beat in scene time falls within the grid's repeating beat, in [0, beatLength).
    internal static double MapOffsetSeconds(double sceneBeatSeconds, double beatLengthSeconds)
    {
        if (!double.IsFinite(sceneBeatSeconds) || !double.IsFinite(beatLengthSeconds) || !(beatLengthSeconds > 0))
        {
            return 0;
        }

        double offset = sceneBeatSeconds % beatLengthSeconds;
        if (offset < 0)
        {
            offset += beatLengthSeconds;
        }

        return offset >= beatLengthSeconds ? 0 : offset;
    }

    // The same options the BPM grid flyout writes: a new tempo and phase, the grid on, and its subdivisions kept.
    internal static BpmGridOptions ApplyTo(BpmGridOptions current, double bpm, double firstBeatOffsetSeconds)
    {
        return new BpmGridOptions(
            bpm,
            current.Subdivisions,
            MediaTime.FromSeconds(Math.Max(0, firstBeatOffsetSeconds)),
            true);
    }
}
