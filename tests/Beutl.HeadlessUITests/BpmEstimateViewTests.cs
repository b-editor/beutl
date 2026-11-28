using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor.Components.Views;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using NAudio.Wave;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class BpmEstimateViewTests
{
    [AvaloniaTest]
    public void Apply_writes_the_estimate_and_enables_the_grid()
    {
        var current = new BpmGridOptions(90, 6, MediaTime.FromSeconds(1), false);
        var view = new BpmEstimateView(current);
        TimelineOptions options = new() { BpmGrid = current };
        view.Applied += (_, grid) => options = options with { BpmGrid = grid };
        using var host = new ControlHost(view, 360, 320);
        view.ShowResult(128, 0.4375);
        HeadlessTestHelpers.Render();

        HeadlessTestHelpers.Click(view.ApplyButton, move: true);

        Assert.Multiple(() =>
        {
            Assert.That(options.BpmGrid.Bpm, Is.EqualTo(128.0));
            Assert.That(options.BpmGrid.Offset.ToSeconds(), Is.EqualTo(0.4375).Within(1e-9));
            Assert.That(options.BpmGrid.IsEnabled, Is.True);
            Assert.That(options.BpmGrid.Subdivisions, Is.EqualTo(6));
        });
    }

    [AvaloniaTest]
    public void Cancel_leaves_the_grid_alone()
    {
        var current = new BpmGridOptions(90, 6, MediaTime.FromSeconds(1), false);
        var view = new BpmEstimateView(current);
        TimelineOptions options = new() { BpmGrid = current };
        view.Applied += (_, grid) => options = options with { BpmGrid = grid };
        using var host = new ControlHost(view, 360, 320);
        view.ShowResult(128, 0.4375);
        HeadlessTestHelpers.Render();

        HeadlessTestHelpers.Click(view.CancelButton, move: true);

        Assert.That(options.BpmGrid, Is.EqualTo(current));
    }

    [AvaloniaTest]
    public async Task Analyzing_a_clip_shows_its_estimate_and_applies_it()
    {
        const double bpm = 110;
        const double trimSeconds = 0.25;
        const double clipStartSeconds = 3;
        string path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory, $"bpm-flyout-{Guid.NewGuid():N}.wav");
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(44100, 1)))
        {
            float[] samples = CreateClickSamples(bpm, 8, 44100);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        try
        {
            var scene = new Scene(1280, 720, "bpm-flyout")
            {
                Duration = { Value = MediaTime.FromSeconds(12) },
                SampleRate = { Value = 44100 },
            };
            var project = new Project();
            project.Items.Add(scene);
            var element = new Element
            {
                Start = { Value = MediaTime.FromSeconds(clipStartSeconds) },
                Length = { Value = MediaTime.FromSeconds(8 - trimSeconds) },
            };
            element.AddObject(new SourceSound
            {
                Source = { Value = SoundSource.Open(path) },
                OffsetPosition = { Value = MediaTime.FromSeconds(trimSeconds) },
            });
            scene.Children.Add(element);
            BpmGridOptions? applied = null;
            var flyout = new BpmEstimateFlyout(
                scene, element, TestShell.FontManager, TestShell.Decoders,
                new BpmGridOptions(90, 6, MediaTime.Zero, false),
                grid => applied = grid);
            var anchor = new Button { Content = "anchor" };
            using var host = new ControlHost(anchor, 420, 420);
            flyout.ShowAt(anchor, true);
            HeadlessTestHelpers.Render();

            await flyout.AnalyzeAsync();
            HeadlessTestHelpers.Render(2);

            Assert.That(flyout.EstimateView.ApplyButton.IsVisible, Is.True);
            HeadlessTestHelpers.Click(flyout.EstimateView.ApplyButton, move: true);

            Assert.That(applied, Is.Not.Null);
            double beatLength = 60 / applied!.Value.Bpm;
            double firstBeatInScene = clipStartSeconds + (beatLength - trimSeconds);
            Assert.Multiple(() =>
            {
                Assert.That(applied.Value.Bpm, Is.EqualTo(bpm).Within(1));
                Assert.That(
                    Math.Abs(Math.IEEERemainder(firstBeatInScene - applied.Value.Offset.ToSeconds(), beatLength)),
                    Is.LessThanOrEqualTo(1d / 30));
                Assert.That(applied.Value.IsEnabled, Is.True);
                Assert.That(applied.Value.Subdivisions, Is.EqualTo(6));
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static float[] CreateClickSamples(double bpm, double durationSeconds, int sampleRate)
    {
        int sampleCount = (int)Math.Round(durationSeconds * sampleRate);
        var samples = new float[sampleCount];
        int beatSamples = (int)Math.Round(sampleRate * 60d / bpm);
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

    [AvaloniaTest]
    public void The_flyout_shows_the_editable_result()
    {
        var flyout = new BpmEstimateFlyout(
            new Scene(), new Element(), TestShell.FontManager, TestShell.Decoders,
            new BpmGridOptions(120, 4, MediaTime.Zero, false), _ => { });
        var anchor = new Button { Content = "anchor" };
        using var host = new ControlHost(anchor, 420, 420);
        flyout.ShowAt(anchor, true);
        HeadlessTestHelpers.Render();
        BpmEstimateView view = flyout.EstimateView;
        view.ShowResult(110, 0.4375);
        HeadlessTestHelpers.Render(3);

        HeadlessCapture.SaveIfRequested(view, string.Empty, "estimate-bpm");

        Assert.Multiple(() =>
        {
            Assert.That(view.BpmInput.Value, Is.EqualTo(110m));
            Assert.That(view.OffsetInput.Value, Is.EqualTo(0.4375m));
            Assert.That(view.ApplyButton.IsVisible, Is.True);
            Assert.That(view.Bounds.Width, Is.GreaterThan(200));
        });
    }
}
