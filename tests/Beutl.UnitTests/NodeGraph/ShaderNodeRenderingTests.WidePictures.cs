using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.NodeGraph.Nodes.Shaders;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.NodeGraph;

public partial class ShaderNodeRenderingTests
{
    // A pass that renders only part of its output and says what part of its source it reads still keeps that source whole.
    // After a grid too wide for one buffer it asks for all of the grid, as a pass that says nothing does, instead of laying
    // the part it reads into all of the grid at the part's density, which no buffer could hold.
    [Test]
    public void APartialPassAfterAPictureWiderThanABufferDrawsLikeAPassThatSaysNothing()
    {
        VulkanTestEnvironment.EnsureAvailable();

        byte[][] plain = RenderAfterAWideGrid(partial: false);

        byte[][] partial = RenderAfterAWideGrid(partial: true);

        // The reference shows the copies: one covers x from 800 to 920 and y from 600 to 680, red on its left half.
        Assert.Multiple(() =>
        {
            Assert.That(plain[640][830 * 4 + 2], Is.GreaterThan(250), "red");
            Assert.That(plain[640][830 * 4 + 3], Is.GreaterThan(250), "alpha");
        });
        (int Difference, int X, int Y) largest = default;
        for (int y = 0; y < partial.Length; y++)
            for (int x = 0; x < partial[y].Length; x++)
            {
                int difference = Math.Abs(partial[y][x] - plain[y][x]);
                if (difference > largest.Difference)
                    largest = (difference, x / 4, y);
            }
        Assert.That(largest.Difference, Is.LessThanOrEqualTo(2), $"at ({largest.X}, {largest.Y})");
    }

    // A red and blue picture 120 x 80 repeated 100 x 100 times every 200 units, then a pass that copies it, in a 1920 x 1080
    // frame. Linear premultiplied BGRA bytes, one array per row.
    private static byte[][] RenderAfterAWideGrid(bool partial)
    {
        var effect = new NodeGraphFilterEffect();
        GraphModel graph = effect.Model.Value!;
        var input = graph.Add(new FilterEffectInputNode());
        var pass = graph.Add(new ShaderFullscreenPassNode());
        if (partial)
        {
            var region = graph.Add(new ShaderInputRegionNode());
            graph.Connect(region.Image, input.Output);
            graph.Connect(pass.In("source"), region.Output);
            graph.Connect(pass.Settings, graph.Add(new ShaderPartialRenderingNode()).Output);
        }
        else
        {
            graph.Connect(pass.In("source"), input.Output);
        }

        var output = graph.Add(new OutputNode());
        graph.Connect(output.InputPort, pass.Out("color"));

        var effects = new FilterEffectGroup();
        effects.Children.Add(new RepeatEffect
        {
            Columns = { Value = 100 },
            Rows = { Value = 100 },
            Spacing = { Value = new Size(80, 120) },
        });
        effects.Children.Add(effect);
        var gradient = new LinearGradientBrush
        {
            StartPoint = { Value = new RelativePoint(0, 0.5f, RelativeUnit.Relative) },
            EndPoint = { Value = new RelativePoint(1, 0.5f, RelativeUnit.Relative) },
        };
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.Red, 0.5f));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 0.5f));
        gradient.GradientStops.Add(new GradientStop(Colors.Blue, 1));
        var picture = new RectShape { Width = { Value = 120 }, Height = { Value = 80 } };
        picture.Fill.Value = gradient;
        picture.FilterEffect.Value = effects;

        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var resource = (Drawable.Resource)picture.ToResource(CompositionContext.Default);
            using Bitmap rendered = GoldenImageHarness.RenderAtScale(resource, new PixelSize(1920, 1080), 1, null, Colors.Transparent);
            using Bitmap bytes = rendered.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Premul, BitmapColorSpace.LinearSrgb);
            return Enumerable.Range(0, bytes.Height).Select(y => bytes.GetRow(y).ToArray()).ToArray();
        });
    }
}
