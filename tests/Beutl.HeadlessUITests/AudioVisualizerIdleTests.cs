using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.AudioVisualizerTab.Views;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// A paused meter redrew and analyzed the same samples 30 times a second, for as long as its tab was open.
[TestFixture]
[NonParallelizable]
public sealed class AudioVisualizerIdleTests
{
    private const int SampleRate = 48000;
    private const int Frames = SampleRate / 2;
    private static readonly MediaTime s_end = MediaTime.FromSamples(Frames, SampleRate);

    private static float[] Sine(float amplitude)
    {
        var samples = new float[Frames * 2];
        for (int i = 0; i < Frames; i++)
            samples[i * 2] = samples[i * 2 + 1] = amplitude * MathF.Sin(2 * MathF.PI * 440 * i / SampleRate);
        return samples;
    }

    private static AudioSampleRingBuffer Ring(float amplitude = 0.5f)
    {
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(Sine(amplitude), 2, SampleRate, MediaTime.Zero);
        return ring;
    }

    [AvaloniaTest]
    public void A_render_is_requested_only_for_new_input()
    {
        AudioSampleRingBuffer ring = Ring();
        var control = new WaveformControl { RingBuffer = ring, PlayheadTime = s_end };

        Assert.That(control.RequestRenderIfChanged(), Is.True, "the first look at the input");
        Assert.That(control.RequestRenderIfChanged(), Is.False);

        ring.WriteInterleaved(Sine(0.25f), 2, SampleRate, s_end);
        Assert.That(control.RequestRenderIfChanged(), Is.True, "new samples");
        Assert.That(control.RequestRenderIfChanged(), Is.False);

        control.PlayheadTime = MediaTime.FromSeconds(0.1);
        Assert.That(control.RequestRenderIfChanged(), Is.True, "another position");
        Assert.That(control.RequestRenderIfChanged(), Is.False);

        ring.Clear();
        Assert.That(control.RequestRenderIfChanged(), Is.True, "cleared samples");
        Assert.That(control.RequestRenderIfChanged(), Is.False);
    }

    [AvaloniaTest]
    public async Task A_shown_visualizer_stops_drawing_until_something_changes()
    {
        AudioSampleRingBuffer ring = Ring();
        var control = new CountingVisualizer { RingBuffer = ring, PlayheadTime = s_end };
        var window = new Window { Content = control, Width = 200, Height = 100 };
        try
        {
            window.Show();
            await Pump();
            int idle = control.Renders;
            Assert.That(idle, Is.GreaterThan(0));

            await Pump();
            Assert.That(control.Renders, Is.EqualTo(idle), "nothing changed");

            ring.WriteInterleaved(Sine(0.25f), 2, SampleRate, s_end);
            await Pump();
            Assert.That(control.Renders, Is.GreaterThan(idle), "new samples");

            idle = control.Renders;
            control.PlayheadTime = MediaTime.FromSeconds(0.1);
            await Pump();
            Assert.That(control.Renders, Is.GreaterThan(idle), "another position");

            idle = control.Renders;
            control.PrimaryBrush = Brushes.Red;
            await Pump();
            Assert.That(control.Renders, Is.GreaterThan(idle), "a setting of the view");
        }
        finally
        {
            window.Close();
        }
    }

    // The hold falls by 0.5 a second, so a meter left at silence keeps drawing for about a second and then stops.
    [AvaloniaTest]
    public void The_meter_keeps_drawing_until_its_holds_have_fallen()
    {
        var meter = new LevelMeterControl { RingBuffer = Ring(), PlayheadTime = s_end };
        using RenderTargetBitmap bitmap = Arrange(meter);
        bitmap.Render(meter);

        meter.PlayheadTime = MediaTime.Zero;
        bitmap.Render(meter);
        Assert.That(meter.IsSettling, Is.True, "the peak hold is falling toward silence");

        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (meter.IsSettling && DateTime.UtcNow < limit)
        {
            Thread.Sleep(30);
            bitmap.Render(meter);
        }

        Assert.That(meter.IsSettling, Is.False);
    }

    // Mastered music peaks at full scale all the time, so a pause often lands on samples that keep the clip light on.
    [AvaloniaTest]
    public void The_meter_rests_on_samples_that_keep_a_clip_light_on()
    {
        var meter = new LevelMeterControl { RingBuffer = Ring(amplitude: 1f), PlayheadTime = s_end };
        using RenderTargetBitmap bitmap = Arrange(meter);
        bitmap.Render(meter);

        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (meter.IsSettling && DateTime.UtcNow < limit)
        {
            Thread.Sleep(30);
            bitmap.Render(meter);
        }

        Assert.That(meter.IsSettling, Is.False);
    }

    // Nothing draws while the samples hold the light on, which must not use up the two seconds it stays on after them.
    [AvaloniaTest]
    public void A_clip_light_held_on_by_paused_samples_starts_its_hold_when_they_let_go()
    {
        AudioSampleRingBuffer ring = Ring(amplitude: 1f);
        var meter = new LevelMeterControl { RingBuffer = ring, PlayheadTime = s_end };
        using RenderTargetBitmap bitmap = Arrange(meter);
        bitmap.Render(meter);
        var limit = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (meter.IsSettling && DateTime.UtcNow < limit)
        {
            Thread.Sleep(30);
            bitmap.Render(meter);
        }
        Assert.That(meter.IsSettling, Is.False);
        Thread.Sleep(2100);

        // Just under the threshold, so that only the light has anything left to do once the rest has settled.
        ring.WriteInterleaved(Sine(0.98f), 2, SampleRate, s_end);
        meter.PlayheadTime = s_end + s_end;
        limit = DateTime.UtcNow + TimeSpan.FromSeconds(0.5);
        while (DateTime.UtcNow < limit)
        {
            bitmap.Render(meter);
            Thread.Sleep(30);
        }
        bitmap.Render(meter);

        Assert.That(meter.IsSettling, Is.True, "the light waits two seconds to go out");
    }

    // The heaviest smoothing takes the smallest steps, which must not pass for having arrived.
    [AvaloniaTest]
    [TestCase(80f)]
    [TestCase(95f)]
    public void The_spectrum_keeps_drawing_until_its_bars_have_settled(float smoothing)
    {
        var spectrum = new SpectrumControl { RingBuffer = Ring(), PlayheadTime = s_end, Smoothing = smoothing };
        using RenderTargetBitmap bitmap = Arrange(spectrum);
        bitmap.Render(spectrum);
        Assert.That(spectrum.IsSettling, Is.True, "the bars start from nothing");

        for (int i = 0; i < 500 && spectrum.IsSettling; i++)
            bitmap.Render(spectrum);

        Assert.That(spectrum.IsSettling, Is.False);
        float[] settled = [.. Bars(spectrum)];
        for (int i = 0; i < 500; i++)
            bitmap.Render(spectrum);
        Assert.That(settled, Is.EqualTo(Bars(spectrum)).Within(0.01f), "where the bars stop is where they were going");

        static float[] Bars(SpectrumControl control) => (float[])typeof(SpectrumControl)
            .GetField("_smoothed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(control)!;
    }

    private static RenderTargetBitmap Arrange(Control control)
    {
        var size = new Size(320, 240);
        control.Measure(size);
        control.Arrange(new Rect(size));
        return new RenderTargetBitmap(new PixelSize(320, 240));
    }

    private static async Task Pump()
    {
        await Task.Delay(150);
        HeadlessTestHelpers.Render();
        await Task.Delay(50);
        HeadlessTestHelpers.Render();
    }

    private sealed class CountingVisualizer : AudioVisualizerControlBase
    {
        public int Renders { get; private set; }

        public override void Render(DrawingContext context) => Renders++;
    }
}
