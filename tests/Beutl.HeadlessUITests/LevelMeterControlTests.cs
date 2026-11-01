using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
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
        var window = new Window { Width = 320, Height = 240, Background = Brushes.Black, Content = meter };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            if (HeadlessCapture.IsRequested)
            {
                // The loudness readout eases in over ~200 ms of wall time; let it settle for the capture.
                for (int i = 0; i < 20; i++)
                {
                    Thread.Sleep(60);
                    meter.InvalidateVisual();
                    HeadlessTestHelpers.Render(1);
                }
            }

            using WriteableBitmap? frame = window.CaptureRenderedFrame();
            Assert.That(frame, Is.Not.Null);

            List<double> centres = LitRunCentres(frame!, (int)(window.Height / 2));

            Assert.That(centres, Has.Count.EqualTo(litBars));
            if (name == "surround51-centre")
            {
                // C is the third of six equal bar slots left of the dB scale.
                double slot = (window.Width - 4 * 3 - 20) / 6;
                Assert.That(centres[0], Is.InRange(4 + slot * 2, 4 + slot * 3 + 10));
            }

            HeadlessCapture.SaveIfRequested(meter, nameof(LevelMeterControlTests), name);
        }
        finally
        {
            window.Close();
        }
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

    private static unsafe List<double> LitRunCentres(WriteableBitmap bitmap, int y)
    {
        using ILockedFramebuffer buffer = bitmap.Lock();
        Assert.That(buffer.Format, Is.EqualTo(PixelFormat.Rgba8888).Or.EqualTo(PixelFormat.Bgra8888));
        var centres = new List<double>();
        uint* row = (uint*)((byte*)buffer.Address + y * buffer.RowBytes);
        int start = -1;
        for (int x = 0; x <= buffer.Size.Width; x++)
        {
            bool lit = false;
            if (x < buffer.Size.Width)
            {
                uint pixel = row[x];
                // The primary brush is green; both byte orders keep green in the second byte.
                uint g = (pixel >> 8) & 0xFF;
                uint rb = Math.Max(pixel & 0xFF, (pixel >> 16) & 0xFF);
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
