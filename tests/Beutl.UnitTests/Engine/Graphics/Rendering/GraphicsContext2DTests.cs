using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

public class GraphicsContext2DTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void ShouldTriggerOnUntrackedEvent()
    {
        var drawable = new RectShape
        {
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center,
            TransformOrigin = RelativePoint.Center,
            Width = 100,
            Height = 100,
            Fill = Brushes.White,
            FilterEffect = new FilterEffectGroup { Children = { new SplitEffect(), new InnerShadow() } },
            Transform = new TransformGroup { Children = { new RotationTransform(), new ScaleTransform() } }
        };

        var node = new DrawableRenderNode(drawable);
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        drawable.Render(context);

        ((FilterEffectGroup)drawable.FilterEffect).Children.RemoveAt(0);
        context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        bool triggered = false;
        RenderNode? untrackedNode = null;
        context.OnUntracked = n =>
        {
            triggered = true;
            untrackedNode = n;
        };
        drawable.Render(context);

        Assert.That(triggered, Is.True);
        Assert.That(untrackedNode, Is.Not.Null);
        Assert.That(untrackedNode, Is.TypeOf<FilterEffectRenderNode>());
    }
}
