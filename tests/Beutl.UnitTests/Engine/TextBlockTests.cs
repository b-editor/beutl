using System.Text;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;

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
        _ = TypefaceProvider.Typeface();
    }

    [Test]
    [TestCase(Case1)]
    [TestCase(Case2)]
    public void ParseAndDraw(string str)
    {
        TextBlock tb = CreateTextBlock(str);
        var resource = tb.ToResource(CompositionContext.Default);

        var node = new DrawableRenderNode(resource);
        using (var context = new GraphicsContext2D(node, new(1920, 1080)))
        {
            context.Clear(Colors.White);
            tb.Render(context, resource);
        }

        using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = new Rect(0, 0, 1920, 1080),
            CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
        });
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        Assert.That(rasterization.IsEmpty, Is.False);
    }

    [Test]
    public void Parse_AppliesEachTagToTheTextItEncloses()
    {
        using TextBlock.Resource resource = CreateTextBlock(Case1).ToResource(CompositionContext.Default);
        TextElements elements = resource.GetTextElements();
        TextElement Element(string text) => elements.Single(element => element.Text == text);

        var lines = new List<string>();
        foreach (Span<FormattedText> line in elements.Lines)
        {
            var text = new StringBuilder();
            foreach (FormattedText item in line)
                text.Append(item.Text.AsSpan());
            lines.Add(text.ToString());
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lines, Is.EqualTo(new[]
            {
                "吾輩は猫である。",
                "名前はまだ無い。",
                "",
                "この文字列は複数行に分かれてます。",
                "",
                "縁取り",
                "Roboto",
                "<font='Noto Sans JP'><bold>Noto Sans</font></bold>",
            }));
            Assert.That(Element("吾輩").FontWeight, Is.EqualTo(FontWeight.Bold));
            Assert.That(Element("吾輩").Size, Is.EqualTo(100));
            Assert.That(Element("は").Size, Is.EqualTo(70));
            Assert.That(Element("無").Size, Is.EqualTo(100));
            Assert.That((Element("猫").Brush as SolidColorBrush.Resource)?.Color, Is.EqualTo(Colors.Red));
            Assert.That(Element("名前").FontStyle, Is.EqualTo(FontStyle.Italic));
            Assert.That(Element("この文字列は").FontWeight, Is.EqualTo(FontWeight.Bold));
            Assert.That(Element("この文字列は").IgnoreLineBreaks, Is.True);
            Assert.That(Element("縁取り").Pen?.Thickness, Is.EqualTo(5));
            Assert.That(Element("縁取り").Pen?.StrokeAlignment, Is.EqualTo(StrokeAlignment.Outside));
            Assert.That(Element("Roboto").FontFamily.Name, Is.EqualTo("Roboto"));
            Assert.That(Element("Noto Sans").FontWeight, Is.EqualTo(FontWeight.Regular));
        }
    }

    private static TextBlock CreateTextBlock(string text)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var tb = new TextBlock();
        tb.FontFamily.CurrentValue = typeface.FontFamily;
        tb.FontStyle.CurrentValue = typeface.Style;
        tb.FontWeight.CurrentValue = typeface.Weight;
        tb.Size.CurrentValue = 100;
        tb.Fill.CurrentValue = Brushes.Black;
        tb.Spacing.CurrentValue = 0;
        tb.Text.CurrentValue = text;
        return tb;
    }
}
