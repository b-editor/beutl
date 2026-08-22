using System.Collections.Immutable;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

[NonParallelizable]
[TestFixture]
public class FrameClearCoverageTests
{
    [TestCase(128, 73, 0.25f, 32, 19)]
    [TestCase(73, 128, 0.25f, 19, 32)]
    [TestCase(1, 1, 0.25f, 1, 1)]
    public void EmptyFrame_ClearCoversEveryDevicePixel(
        int width,
        int height,
        float outputScale,
        int expectedDeviceWidth,
        int expectedDeviceHeight)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using RenderTarget target = RenderTarget.Create(expectedDeviceWidth, expectedDeviceHeight)
                ?? throw new InvalidOperationException("RenderTarget.Create returned null.");
            using (var prefill = new ImmediateCanvas(target))
            {
                prefill.Clear(Colors.Magenta);
            }

            using var renderer = new Renderer(
                width,
                height,
                RenderIntent.Preview,
                outputScale,
                float.PositiveInfinity,
                target);
            var frame = new CompositionFrame(
                ImmutableArray<EngineObject.Resource>.Empty,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(width, height),
                null);

            renderer.Render(frame);
            using Bitmap snapshot = renderer.Snapshot();
            ReadOnlySpan<ushort> channels = snapshot.GetPixelSpan<ushort>();
            int nonZeroChannels = 0;
            int firstNonZeroChannel = -1;
            for (int i = 0; i < channels.Length; i++)
            {
                if (channels[i] == 0)
                    continue;

                nonZeroChannels++;
                if (firstNonZeroChannel < 0)
                    firstNonZeroChannel = i;
            }

            Assert.Multiple(() =>
            {
                Assert.That(renderer.DeviceSize, Is.EqualTo(new PixelSize(expectedDeviceWidth, expectedDeviceHeight)));
                Assert.That(
                    nonZeroChannels,
                    Is.Zero,
                    $"the root clear left channel {firstNonZeroChannel} undefined in the outward-rounded device surface");
            });
        });
    }
}
