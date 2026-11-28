using Beutl.Audio;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using NAudio.Wave;

namespace Beutl.UnitTests.Editor.Services;

[TestFixture]
public sealed class BpmEstimateServiceTests
{
    private const int SampleRate = 44100;
    private const double TrimSeconds = 0.25;
    private const double ClipStartSeconds = 3;
    private const double SourceSeconds = 8;
    private readonly List<string> _files = [];
    private DecoderRegistry _decoders = null!;

    [SetUp]
    public void SetUp() => _decoders = TestMediaHelper.CreateRegistry();

    [TearDown]
    public void TearDown()
    {
        foreach (string file in _files)
        {
            File.Delete(file);
        }

        _files.Clear();
    }

    [TestCase(120)]
    [TestCase(110)]
    public async Task A_trimmed_clip_keeps_its_beats_on_the_grid(double bpm)
    {
        // No recorded music fixture with a known tempo ships with the tests, so this follows the analyzer's
        // existing click-track fixture instead.
        string path = CreateClickTrack(bpm, SourceSeconds);
        (Scene scene, Element element) = CreateClip(path);
        double beatLength = 60 / bpm;
        // The source's first beat is at 0, so after dropping the first TrimSeconds the next one is at beatLength.
        double firstBeatInScene = ClipStartSeconds + (beatLength - TrimSeconds);

        BpmEstimate? estimate = await BpmEstimateService.AnalyzeAsync(
            scene, element, TestFonts.Manager, _decoders, CancellationToken.None);

        Assert.That(estimate, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(estimate!.Value.Bpm, Is.EqualTo(bpm).Within(1));
            Assert.That(
                Math.Abs(Math.IEEERemainder(firstBeatInScene - estimate.Value.FirstBeatOffsetSeconds, beatLength)),
                Is.LessThanOrEqualTo(1d / 30),
                "The first beat in scene time must sit on a grid line.");
        });
    }

    [Test]
    public async Task A_clip_without_audio_is_not_analyzed()
    {
        var scene = new Scene(1280, 720, "bpm-silent") { Duration = { Value = MediaTime.FromSeconds(10) } };
        var element = new Element { Length = { Value = MediaTime.FromSeconds(2) } };
        scene.Children.Add(element);

        BpmEstimate? estimate = await BpmEstimateService.AnalyzeAsync(
            scene, element, TestFonts.Manager, _decoders, CancellationToken.None);

        Assert.That(estimate, Is.Null);
    }

    [Test]
    public void An_estimate_turns_the_grid_on_but_keeps_its_subdivisions()
    {
        var current = new BpmGridOptions(90, 6, MediaTime.FromSeconds(1), false);

        BpmGridOptions applied = BpmEstimateService.ApplyTo(current, 128.0, 0.4375);

        Assert.Multiple(() =>
        {
            Assert.That(applied.Bpm, Is.EqualTo(128.0));
            Assert.That(applied.Offset, Is.EqualTo(MediaTime.FromSeconds(0.4375)));
            Assert.That(applied.IsEnabled, Is.True);
            Assert.That(applied.Subdivisions, Is.EqualTo(6));
        });
    }

    [Test]
    public void An_offset_wraps_into_one_beat_from_any_scene_time()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BpmEstimateService.MapOffsetSeconds(3.25, 0.5), Is.EqualTo(0.25).Within(1e-9));
            Assert.That(BpmEstimateService.MapOffsetSeconds(3.21875, 0.46875), Is.EqualTo(0.40625).Within(1e-9));
            Assert.That(BpmEstimateService.MapOffsetSeconds(-0.1, 0.5), Is.EqualTo(0.4).Within(1e-9));
            Assert.That(BpmEstimateService.MapOffsetSeconds(1, 0), Is.Zero);
        });
    }

    private (Scene Scene, Element Element) CreateClip(string path)
    {
        var scene = new Scene(1280, 720, "bpm")
        {
            Duration = { Value = MediaTime.FromSeconds(12) },
            SampleRate = { Value = SampleRate },
        };
        var project = new Project();
        project.Items.Add(scene);
        var element = new Element
        {
            Start = { Value = MediaTime.FromSeconds(ClipStartSeconds) },
            Length = { Value = MediaTime.FromSeconds(SourceSeconds - TrimSeconds) },
        };
        var sound = new SourceSound
        {
            Source = { Value = SoundSource.Open(path) },
            OffsetPosition = { Value = MediaTime.FromSeconds(TrimSeconds) },
        };
        element.AddObject(sound);
        scene.Children.Add(element);
        return (scene, element);
    }

    // Float PCM with a short click on every beat.
    private string CreateClickTrack(double bpm, double durationSeconds)
    {
        string path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory, $"bpm-click-{bpm:F0}-{Guid.NewGuid():N}.wav");
        float[] samples = CreateClickSamples(bpm, durationSeconds);
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        _files.Add(path);
        return path;
    }

    private static float[] CreateClickSamples(double bpm, double durationSeconds)
    {
        int sampleCount = (int)Math.Round(durationSeconds * SampleRate);
        var samples = new float[sampleCount];
        int beatSamples = (int)Math.Round(SampleRate * 60d / bpm);
        const int clickLength = 72;
        for (int beatStart = 0; beatStart < sampleCount; beatStart += beatSamples)
        {
            for (int i = 0; i < clickLength && beatStart + i < sampleCount; i++)
            {
                samples[beatStart + i] = (float)(1d - (i / (double)clickLength));
            }
        }

        return samples;
    }
}
