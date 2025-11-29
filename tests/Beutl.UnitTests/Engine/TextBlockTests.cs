using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Pixel;
using Microsoft.Extensions.Logging;
using NUnit.Framework.Legacy;

namespace Beutl.UnitTests.Engine;

public class TextBlockTests
{
    private const string Case1 = @"<b>吾輩</b><size=70>は</size><#ff0000>猫</#><size=70>である。</size>
<i>名前</i><size=70>はまだ</size>無<size=70>い。</cspace>

<single-line>
<b>この文字列は</b>
複数行に分かれて
ます。
</single-line>

<stroke='Gray,5,flat,miter,outside,2'>縁取り</stroke>
<font='Roboto'>Roboto</font>
<noparse><font='Noto Sans JP'><bold>Noto Sans</font></bold></noparse>";
    private const string Case2 = "吾輩は猫である";

    [SetUp]
    public void Setup()
    {
        Log.LoggerFactory = LoggerFactory.Create(b => b.AddSimpleConsole());
        _ = TypefaceProvider.Typeface();
    }

    [Test]
    [TestCase(Case1, 0)]
    [TestCase(Case2, 1)]
    public void ParseAndDraw(string str, int id)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var tb = new TextBlock();
        tb.FontFamily.CurrentValue = typeface.FontFamily;
        tb.FontStyle.CurrentValue = typeface.Style;
        tb.FontWeight.CurrentValue = typeface.Weight;
        tb.Size.CurrentValue = 100;
        tb.Fill.CurrentValue = Brushes.Black;
        tb.Spacing.CurrentValue = 0;
        tb.Text.CurrentValue = str;

        var resource = tb.ToResource(RenderContext.Default);

        var node = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(node, new(1920, 1080)))
        {
            context.Clear(Colors.White);
            tb.Render(context, resource);
        }

        var processor = new RenderNodeProcessor(node, false);
        using Bitmap<Bgra8888> bmp = processor.RasterizeAndConcat();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"{id}.png"), EncodedImageFormat.Png));
    }

    [Test]
    public void ToSKPath()
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var tb = new TextBlock();
        tb.FontFamily.CurrentValue = typeface.FontFamily;
        tb.FontStyle.CurrentValue = typeface.Style;
        tb.FontWeight.CurrentValue = typeface.Weight;
        tb.Size.CurrentValue = 100;
        tb.Fill.CurrentValue = Brushes.White;
        tb.Spacing.CurrentValue = 0;
        tb.Text.CurrentValue = Case1;
        var resource = tb.ToResource(RenderContext.Default);

        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.Black;
        pen.Thickness.CurrentValue = 5;
        pen.StrokeAlignment.CurrentValue = StrokeAlignment.Outside;
        var penResource = pen.ToResource(RenderContext.Default);

        using var skpath = TextBlock.ToSKPath(resource.GetTextElements());
        var bounds = PenHelper.GetBounds(skpath.Bounds.ToGraphicsRect(), penResource);

        using var renderTarget = RenderTarget.Create((int)bounds.Width, (int)bounds.Height)!;
        using var graphics = new ImmediateCanvas(renderTarget);

        graphics.Clear(Colors.White);
        graphics.DrawSKPath(skpath, true, null, penResource);

        using Bitmap<Bgra8888> bmp = renderTarget.Snapshot();

        ClassicAssert.IsTrue(bmp.Save(Path.Combine(ArtifactProvider.GetArtifactDirectory(), $"0.png"), EncodedImageFormat.Png));
    }
}
