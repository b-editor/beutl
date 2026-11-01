using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.AudioVisualizerTab.Views;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class LevelMeterControlTests
{
    private const int SampleRate = 48000;
    private const int Frames = SampleRate / 2;

    // Bars reach the middle row only when their channel carries signal, so counting the
    // lit runs across it counts the lit bars.
    [AvaloniaTest]
    [TestCase(2, new[] { 0.5f, 0.5f }, 2, "stereo")]
    [TestCase(6, new[] { 0f, 0f, 0.5f, 0f, 0f, 0f }, 1, "surround51-centre")]
    [TestCase(6, new[] { 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.4f }, 6, "surround51")]
    [TestCase(8, new[] { 0.9f, 0.8f, 0.7f, 0f, 0.6f, 0.5f, 0.4f, 0.3f }, 7, "surround71")]
    public void DrawsOneBarPerSourceChannel(int channels, float[] amplitudes, int litBars, string name)
    {
        var ring = new AudioSampleRingBuffer();
        ring.WriteInterleaved(Sine(channels, amplitudes), channels, SampleRate, MediaTime.Zero);
        var meter = new LevelMeterControl { RingBuffer = ring, PlayheadTime = MediaTime.FromSamples(Frames, SampleRate) };
        var size = new Size(320, 240);
        // Render the control off-tree: in a window its 33 ms repaint timer keeps the dispatcher busy whenever
        // a repaint takes longer than that (as under coverage on CI), so settling never returns.
        meter.Measure(size);
        meter.Arrange(new Rect(size));
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 240));
        bitmap.Render(meter);
        if (HeadlessCapture.IsRequested)
        {
            // The loudness readout eases in over ~200 ms of wall time; let it settle for the capture.
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(60);
                bitmap.Render(meter);
            }
        }

        List<double> centres = LitRunCentres(bitmap, (int)(size.Height / 2));

        Assert.That(centres, Has.Count.EqualTo(litBars));
        if (name == "surround51-centre")
        {
            // C is the third of six equal bar slots left of the dB scale.
            double slot = (size.Width - 4 * 3 - 20) / 6;
            Assert.That(centres[0], Is.InRange(4 + slot * 2, 4 + slot * 3 + 10));
        }

        SaveIfRequested(bitmap, name);
    }

    private static void SaveIfRequested(RenderTargetBitmap bitmap, string name)
    {
        if (Environment.GetEnvironmentVariable("BEUTL_HEADLESS_CAPTURE_DIR") is not { Length: > 0 } root) return;
        string directory = Path.Combine(root, nameof(LevelMeterControlTests));
        Directory.CreateDirectory(directory);
        // Composite onto black, as the meter appears in the dark editor theme.
        var frame = new Border { Background = Brushes.Black, Child = new Image { Source = bitmap } };
        frame.Measure(bitmap.Size);
        frame.Arrange(new Rect(bitmap.Size));
        using var output = new RenderTargetBitmap(bitmap.PixelSize);
        output.Render(frame);
        output.Save(Path.Combine(directory, name + ".png"));
    }

    private static float[] Sine(int channels, float[] amplitudes)
    {
        var samples = new float[channels * Frames];
        for (int f = 0; f < Frames; f++)
        {
            float s = (float)Math.Sin(2 * Math.PI * 997 * f / SampleRate);
            for (int ch = 0; ch < channels; ch++) samples[f * channels + ch] = s * amplitudes[ch];
        }

        return samples;
    }

    private static List<double> LitRunCentres(RenderTargetBitmap bitmap, int y)
    {
        int width = bitmap.PixelSize.Width;
        var row = new byte[width * 4];
        unsafe
        {
            fixed (byte* p = row)
            {
                bitmap.CopyPixels(new PixelRect(0, y, width, 1), (nint)p, row.Length, row.Length);
            }
        }

        var centres = new List<double>();
        int start = -1;
        for (int x = 0; x <= width; x++)
        {
            bool lit = false;
            if (x < width)
            {
                // The primary brush is green; both byte orders keep green in the second byte.
                int g = row[x * 4 + 1];
                int rb = Math.Max(row[x * 4], row[x * 4 + 2]);
                lit = g > 150 && g > rb + 40;
            }

            if (lit && start < 0) start = x;
            else if (!lit && start >= 0)
            {
                centres.Add((start + x - 1) / 2.0);
                start = -1;
            }
        }

        return centres;
    }
}
