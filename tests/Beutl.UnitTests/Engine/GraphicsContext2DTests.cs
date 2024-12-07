using System.CodeDom.Compiler;
using System.Text;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.V2;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Logging;
using Beutl.Media;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Engine;

public class GraphicsContext2DTests
{
    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
    }

    [Test]
    public void Test()
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
            Transform = new TransformGroup() { Children = { new RotationTransform(), new ScaleTransform() } }
        };

        var node = new DrawableRenderNode(drawable);
        var context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        drawable.Render(new GraphicsContext2D.CanvasImpl(context));

        var sb = new StringBuilder();
        DisplayNode(node, sb);
        Console.WriteLine(sb.ToString());

        ((FilterEffectGroup)drawable.FilterEffect).Children.RemoveAt(0);
        context = new GraphicsContext2D(node, new PixelSize(1920, 1080));
        context.OnUntracked = (n) => Console.WriteLine($"Untracked: {n.GetType().Name}");
        drawable.Render(new GraphicsContext2D.CanvasImpl(context));

        sb = new StringBuilder();
        DisplayNode(node, sb);
        Console.WriteLine(sb.ToString());
    }

    private void DisplayNode(RenderNode node, StringBuilder sb, int indent = 0)
    {
        if (node is ContainerRenderNode containerRenderNode)
        {
            sb.AppendLine($"{new string(' ', indent)}<{containerRenderNode.GetType().Name}>");
            foreach (var child in containerRenderNode.Children)
            {
                DisplayNode(child, sb, indent + 2);
            }

            sb.AppendLine($"{new string(' ', indent)}</{containerRenderNode.GetType().Name}>");
        }
        else
        {
            sb.AppendLine($"{new string(' ', indent)}<{node.GetType().Name} />");
        }
    }
}
