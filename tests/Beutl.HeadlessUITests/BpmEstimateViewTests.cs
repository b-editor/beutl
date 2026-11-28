using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor.Components.Views;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using NAudio.Wave;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class BpmEstimateViewTests
{
    [AvaloniaTest]
    public void Confirm_writes_the_estimate_and_enables_the_grid()
    {
        var current = new BpmGridOptions(90, 6, MediaTime.FromSeconds(1), false);
        BpmGridOptions? applied = null;
        var (flyout, host) = ShowEstimate(current, grid => applied = grid);
        using (host)
        {
            flyout.EstimateView.ShowResult(128, 0.4375);
            HeadlessTestHelpers.Render(2);

            Assert.That(flyout.ConfirmButton, Is.Not.Null);
            Assert.That(flyout.ConfirmButton!.IsEnabled, Is.True);
            HeadlessTestHelpers.Click(flyout.ConfirmButton, move: true);

            Assert.That(applied, Is.Not.Null);
            BpmGridOptions result = applied!.Value;
            Assert.Multiple(() =>
            {
                Assert.That(result.Bpm, Is.EqualTo(128.0));
                Assert.That(result.Offset.ToSeconds(), Is.EqualTo(0.4375).Within(1e-9));
                Assert.That(result.IsEnabled, Is.True);
                Assert.That(result.Subdivisions, Is.EqualTo(6));
            });
        }
    }

    [AvaloniaTest]
    public void Dismiss_leaves_the_grid_alone()
    {
        var current = new BpmGridOptions(90, 6, MediaTime.FromSeconds(1), false);
        BpmGridOptions? applied = null;
        var (flyout, host) = ShowEstimate(current, grid => applied = grid);
        using (host)
        {
            flyout.EstimateView.ShowResult(128, 0.4375);
            HeadlessTestHelpers.Render(2);

            Assert.That(flyout.DismissButton, Is.Not.Null);
            HeadlessTestHelpers.Click(flyout.DismissButton!, move: true);

            Assert.That(applied, Is.Null);
            Assert.That(flyout.EstimateView.CanConfirm, Is.True);
        }
    }

    [AvaloniaTest]
    public void Confirm_is_disabled_while_analyzing()
    {
        var (flyout, host) = ShowEstimate(
            new BpmGridOptions(90, 6, MediaTime.Zero, false), _ => { });
        using (host)
        {
            flyout.EstimateView.ShowAnalyzing();
            HeadlessTestHelpers.Render(2);

            Assert.Multiple(() =>
            {
                Assert.That(flyout.ConfirmButton, Is.Not.Null);
                Assert.That(flyout.ConfirmButton!.IsEnabled, Is.False);
                Assert.That(flyout.EstimateView.CanConfirm, Is.False);
                Assert.That(flyout.EstimateView.Fields.IsEnabled, Is.False);
            });
        }
    }

    [AvaloniaTest]
    public void Confirm_is_disabled_on_error()
    {
        var (flyout, host) = ShowEstimate(
            new BpmGridOptions(90, 6, MediaTime.Zero, false), _ => { });
        using (host)
        {
            flyout.EstimateView.ShowError(string.Join(' ', Enumerable.Repeat("failed", 40)));
            HeadlessTestHelpers.Render(2);

            Assert.Multiple(() =>
            {
                Assert.That(flyout.ConfirmButton, Is.Not.Null);
                Assert.That(flyout.ConfirmButton!.IsEnabled, Is.False);
                Assert.That(flyout.EstimateView.CanConfirm, Is.False);
                Assert.That(flyout.EstimateView.Fields.IsEnabled, Is.False);
                Assert.That(flyout.EstimateView.StatusText.TextWrapping, Is.EqualTo(Avalonia.Media.TextWrapping.Wrap));
                Assert.That(
                    flyout.EstimateView.StatusText.Bounds.Width,
                    Is.LessThanOrEqualTo(flyout.EstimateView.Bounds.Width));
            });
        }
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
            var (flyout, host) = ShowEstimate(
                new BpmGridOptions(90, 6, MediaTime.Zero, false), grid => applied = grid, scene, element);
            using (host)
            {
                await flyout.AnalyzeAsync();
                HeadlessTestHelpers.Render(2);

                Assert.That(flyout.ConfirmButton!.IsEnabled, Is.True);
                HeadlessTestHelpers.Click(flyout.ConfirmButton, move: true);

                Assert.That(applied, Is.Not.Null);
                BpmGridOptions result = applied!.Value;
                double beatLength = 60 / result.Bpm;
                double firstBeatInScene = clipStartSeconds + (beatLength - trimSeconds);
                Assert.Multiple(() =>
                {
                    Assert.That(result.Bpm, Is.EqualTo(bpm).Within(1));
                    Assert.That(
                        Math.Abs(Math.IEEERemainder(firstBeatInScene - result.Offset.ToSeconds(), beatLength)),
                        Is.LessThanOrEqualTo(1d / 30));
                    Assert.That(result.IsEnabled, Is.True);
                    Assert.That(result.Subdivisions, Is.EqualTo(6));
                });
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaTest]
    public void The_flyout_shows_the_editable_result_in_the_grid_layout()
    {
        var (flyout, host) = ShowEstimate(
            new BpmGridOptions(120, 4, MediaTime.Zero, false), _ => { });
        using (host)
        {
            flyout.EstimateView.ShowResult(110, 0.4375);
            HeadlessTestHelpers.Render(2);

            BpmEstimateView view = flyout.EstimateView;
            Assert.Multiple(() =>
            {
                Assert.That(view.BpmInput.Value, Is.EqualTo(110m));
                Assert.That(view.BpmInput.MinWidth, Is.EqualTo(140));
                Assert.That(view.BpmInput.FormatString, Is.EqualTo("F1"));
                Assert.That(view.OffsetInput.Value, Is.EqualTo(0.4375m));
                Assert.That(view.OffsetInput.MinWidth, Is.EqualTo(140));
                Assert.That(view.OffsetInput.FormatString, Is.EqualTo("F3"));
                Assert.That(view.Fields.Children, Has.Count.EqualTo(4));
                Assert.That(flyout.ConfirmButton!.IsEnabled, Is.True);
            });
        }
    }

    [AvaloniaTest]
    public void The_grid_and_estimate_flyouts_share_the_same_presentation()
    {
        var anchor = CreateAnchor();
        var root = new Panel { Children = { anchor } };
        using var host = new ControlHost(root, 420, 420);

        var gridFlyout = new BpmGridFlyout
        {
            IsEnabledChecked = true,
            Bpm = 110m,
            Subdivisions = 4,
            OffsetSeconds = 0.4375m,
        };
        gridFlyout.ShowAt(anchor, true);
        HeadlessTestHelpers.Render(3);
        var gridPresenter = (FAPickerFlyoutPresenter)gridFlyout.Popup.Child!;
        HeadlessCapture.SaveIfRequested(gridPresenter, string.Empty, "bpm-grid-flyout");
        gridFlyout.Hide();
        HeadlessTestHelpers.Render(2);

        var (estimateFlyout, estimateHost) = ShowEstimate(
            new BpmGridOptions(110, 4, MediaTime.Zero, false), _ => { });
        using (estimateHost)
        {
            estimateFlyout.EstimateView.ShowResult(110, 0.4375);
            HeadlessTestHelpers.Render(3);
            var estimatePresenter = (FAPickerFlyoutPresenter)estimateFlyout.Popup.Child!;
            HeadlessCapture.SaveIfRequested(estimatePresenter, string.Empty, "estimate-bpm");

            Assert.Multiple(() =>
            {
                Assert.That(estimatePresenter.Width, Is.EqualTo(gridPresenter.Width));
                Assert.That(estimatePresenter.Padding, Is.EqualTo(gridPresenter.Padding));
                Assert.That(estimateFlyout.ConfirmButton!.IsEnabled, Is.True);
            });
        }
    }

    private static (BpmEstimateFlyout Flyout, ControlHost Host) ShowEstimate(
        BpmGridOptions current,
        Action<BpmGridOptions> apply,
        Scene? scene = null,
        Element? element = null)
    {
        var flyout = new BpmEstimateFlyout(
            scene ?? new Scene(), element ?? new Element(), TestShell.FontManager, TestShell.Decoders, current, apply);
        var anchor = CreateAnchor();
        var host = new ControlHost(new Panel { Children = { anchor } }, 420, 420);
        flyout.ShowAt(anchor, true);
        HeadlessTestHelpers.Render(3);
        return (flyout, host);
    }

    private static Button CreateAnchor()
    {
        return new Button
        {
            Content = "anchor",
            Width = 80,
            Height = 30,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
        };
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
}
