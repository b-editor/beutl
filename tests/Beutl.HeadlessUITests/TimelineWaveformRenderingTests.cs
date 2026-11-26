using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class TimelineWaveformRenderingTests
{
    [AvaloniaTest]
    public void WaveformBatchesDrawDistinctPeaksAndRefreshPreviewGeometry()
    {
        var waveform = new WaveformControl { ChunkCount = 4, WaveformBrush = Brushes.White };
        var window = new Window
        {
            Width = 120,
            Height = 40,
            WindowDecorations = WindowDecorations.None,
            Background = Brushes.Black,
            Content = waveform,
        };
        window.Show();
        try
        {
            waveform.SetChunkBatch([new WaveformChunk(0, -0.75f, 0.5f), new WaveformChunk(2, 0.25f, 0.75f)]);
            HeadlessTestHelpers.Render(2);
            using (var frame = window.CaptureRenderedFrame()!)
            {
                Assert.That(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(15, 24)), Is.EqualTo((255, 255, 255)));
                Assert.That(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(45, 24)), Is.EqualTo((0, 0, 0)));
                Assert.That(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(75, 9)), Is.EqualTo((255, 255, 255)));
                Assert.That(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(75, 24)), Is.EqualTo((0, 0, 0)));
            }
            HeadlessCapture.SaveIfRequested(window, nameof(TimelineWaveformRenderingTests), "waveform");

            waveform.IsPreviewActive = true;
            waveform.SetChunkBatch([new WaveformChunk(2, -0.85f, -0.25f)]);
            HeadlessTestHelpers.Render(2);
            using (var frame = window.CaptureRenderedFrame()!)
            {
                Assert.That(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(75, 9)), Is.EqualTo((0, 0, 0)));
                Assert.That(HeadlessTestHelpers.ReadPixel(frame, new PixelPoint(75, 29)), Is.EqualTo((255, 255, 255)));
            }
            HeadlessCapture.SaveIfRequested(window, nameof(TimelineWaveformRenderingTests), "waveform-preview");
        }
        finally
        {
            window.Close();
        }
    }
}
