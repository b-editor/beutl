using System.Diagnostics;
using Beutl.Audio;
using Beutl.Audio.Graph;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using NAudio.Wave;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class SilenceWaveformTests
{
    [Test]
    public async Task FullCoverage_DetectsShortBurstInOppositePhaseStereo_AtNonZeroStart()
    {
        var sound = new SilenceProbeSound { TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(10), MediaTime.FromSeconds(2)) } };
        var chunks = new List<WaveformChunk>();
        await foreach (var chunk in GetChunksAsync(sound, 100)) chunks.Add(chunk);
        Assert.That(chunks, Has.Count.EqualTo(100));
        Assert.That(chunks[49].MaxValue, Is.EqualTo(0.5f));
        Assert.That(chunks[49].MinValue, Is.EqualTo(-0.5f));
        Assert.That(chunks[50].MaxValue, Is.EqualTo(0.5f));
        Assert.That(chunks[48].MaxValue, Is.Zero);
        var ranges = SilenceDetector.Detect(chunks, MediaTime.FromSeconds(2), -40, MediaTime.FromSeconds(0.3), MediaTime.Zero);
        Assert.That(ranges, Has.Count.EqualTo(2));
        Assert.That(ranges[0].End, Is.EqualTo(MediaTime.FromSeconds(0.98)));
        Assert.That(ranges[1].Start, Is.EqualTo(MediaTime.FromSeconds(1.02)));
    }

    [Test]
    public async Task SceneSound_ComposesReferencedAudio_AndCancellationStopsReading()
    {
        var nested = new Scene(100, 100, "waveform") { Duration = { Value = MediaTime.FromSeconds(2) } };
        var element = new Element { Length = { Value = MediaTime.FromSeconds(2) } };
        element.Objects.Add(new SilenceProbeSound());
        nested.Children.Add(element);
        var sound = new SceneSound
        {
            ReferencedScene = { Value = nested },
            TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2)) }
        };
        var chunks = new List<WaveformChunk>();
        await foreach (var chunk in GetChunksAsync(sound, 100)) chunks.Add(chunk);
        Assert.That(chunks.Any(c => c.MaxValue >= 0.5f), Is.True);
        using var cts = new CancellationTokenSource();
        await using var reader = GetChunksAsync(sound, 100, cts.Token).GetAsyncEnumerator();
        cts.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => { await reader.MoveNextAsync(); });
    }

    [Test]
    public async Task ThirtyMinuteSource_FullCoverageBenchmarksAcquisition()
    {
        TestMediaHelper.RegisterTestDecoder();
        string path = TestMediaHelper.CreateTestAudioFile(durationSeconds: 1800);
        var source = new SoundSource();
        source.ReadFrom(new Uri(path));
        var sound = new SourceSound
        {
            Source = { Value = source },
            TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1800)) }
        };
        var watch = Stopwatch.StartNew();
        int count = 0;
        await foreach (var chunk in GetChunksAsync(sound, 90000))
        {
            Assert.That(chunk.MaxValue, Is.GreaterThan(0.4f));
            count++;
        }
        TestContext.Out.WriteLine($"30 minute source acquisition (generated stereo PCM): {watch.Elapsed.TotalSeconds:F2} s, {count} bins");
        Assert.That(count, Is.EqualTo(90000));
    }

    private static IAsyncEnumerable<WaveformChunk> GetChunksAsync(Sound sound, int count, CancellationToken ct = default)
    {
        var scene = new Scene(100, 100, "silence-waveform");
        var project = new Project { SampleRate = { Value = 48000 } };
        project.Items.Add(scene);
        var element = new Element { Start = { Value = sound.TimeRange.Value.Start }, Length = { Value = sound.TimeRange.Value.Duration } };
        element.Objects.Add(sound);
        scene.Children.Add(element);
        return SilenceWaveform.GetChunksAsync(scene, element, count, ct);
    }

    [Test, Explicit("Writes a 30 minute PCM file for an end-to-end throughput measurement.")]
    public async Task ThirtyMinuteWaveFile_BenchmarksDecodeAndDetection()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-silence-benchmark-{Guid.NewGuid():N}.wav");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            const int rate = 48000;
            var tone = new float[rate * 2];
            var silence = new float[tone.Length];
            for (int i = 0; i < rate; i++)
                tone[i * 2] = tone[i * 2 + 1] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * i / rate);
            using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)))
                for (int second = 0; second < 1800; second++)
                    writer.WriteSamples(second % 5 == 4 ? silence : tone, 0, tone.Length);
            var source = new SoundSource();
            source.ReadFrom(new Uri(path));
            var sound = new SourceSound
            {
                Source = { Value = source },
                TimeRange = { Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(1800)) }
            };
            var watch = Stopwatch.StartNew();
            var chunks = new List<WaveformChunk>();
            await foreach (var chunk in GetChunksAsync(sound, 90000)) chunks.Add(chunk);
            var ranges = SilenceDetector.Detect(chunks, sound.TimeRange.Value.Duration, -40, MediaTime.FromSeconds(0.3), MediaTime.FromSeconds(0.1));
            TestContext.Out.WriteLine($"30 minute stereo WAV (48 kHz): {watch.Elapsed.TotalSeconds:F2} s, {ranges.Count} silence intervals");
            Assert.That(ranges, Has.Count.EqualTo(360));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }
}

public sealed partial class SilenceProbeSound : Sound
{
    public SilenceProbeSound() => ScanProperties<SilenceProbeSound>();

    public override void Compose(AudioContext context, Sound.Resource resource)
    {
        var source = context.AddNode(new ProbeNode());
        var clip = context.CreateClipNode(TimeRange.Value.Start, TimeRange.Value.Duration);
        context.Connect(source, clip);
        context.MarkAsOutput(clip);
    }

    public partial class Resource
    {
        public override SoundSource.Resource? GetSoundSource() => null;
    }

    private sealed class ProbeNode : AudioNode
    {
        public override AudioBuffer Process(AudioProcessContext context)
        {
            var buffer = new AudioBuffer(context.SampleRate, 2, context.GetSampleCount());
            long first = context.TimeRange.Start.ToSamples(context.SampleRate, MidpointRounding.ToZero);
            var left = buffer.GetChannelData(0);
            var right = buffer.GetChannelData(1);
            for (int i = 0; i < buffer.SampleCount; i++)
            {
                double time = (first + i) / (double)context.SampleRate;
                // Sound gets clip-local windows. This burst crosses a batch boundary and sits
                // late in its first envelope bin, where capped thumbnail sampling could miss it.
                if (time is >= 0.995 and < 1.005) { left[i] = 0.5f; right[i] = -0.5f; }
            }
            return buffer;
        }
    }
}
