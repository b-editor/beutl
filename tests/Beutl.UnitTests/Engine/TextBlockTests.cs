using System.Text;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Composition;
using Beutl.NodeGraph.Nodes;
using Beutl.Serialization;

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
        using var resource = tb.ToResource(CompositionContext.Default);

        using var node = new DrawableRenderNode(resource);
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

    [Test]
    public void RenderedLines_AlignToTheLongestLine(
        [Values] TextAlignment alignment, [Values] bool split, [Values(0f, 6f)] float spacing)
    {
        // Matching first/last glyphs make the ink edges comparable even with different styles inside a line.
        TextBlock block = CreateTextBlock("HH\nHHHHHHHH\n\nH<b>H</b><cspace=12>H</cspace>H\nH", 40);
        block.TextAlignment.Value = alignment;
        block.SplitByCharacters.Value = split;
        block.Spacing.Value = spacing;
        using TextBlock.Resource resource = block.ToResource(CompositionContext.Default);
        using var node = new DrawableRenderNode(resource);
        Record(block, resource, node);

        Rect[] lines = RenderLineBounds(node);

        Assert.That(lines, Has.Length.EqualTo(4));
        Assert.That(lines[1].Width, Is.GreaterThan(lines[0].Width));
        foreach (Rect line in lines)
        {
            float anchor = alignment switch
            {
                TextAlignment.Center => line.Center.X,
                TextAlignment.End => line.Right,
                _ => line.Left,
            };
            float expected = alignment switch
            {
                TextAlignment.Center => lines[1].Center.X,
                TextAlignment.End => lines[1].Right,
                _ => lines[1].Left,
            };
            Assert.That(anchor, Is.EqualTo(expected).Within(1), $"{alignment}, split={split}, spacing={spacing}");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChangingAlignment_UpdatesAnExistingRenderTree(bool split)
    {
        TextBlock block = CreateTextBlock("HHHHHH\nHH", 40);
        block.SplitByCharacters.Value = split;
        using TextBlock.Resource resource = block.ToResource(CompositionContext.Default);
        using var node = new DrawableRenderNode(resource);
        Record(block, resource, node);
        Rect[] before = RenderLineBounds(node);
        Size size = block.MeasureInternal(Size.Infinity, resource);
        int version = resource.Version;

        block.TextAlignment.Value = TextAlignment.End;
        bool changed = false;
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        node.Update(resource);
        Record(block, resource, node);
        Rect[] after = RenderLineBounds(node);

        Assert.Multiple(() =>
        {
            Assert.That(resource.Version, Is.GreaterThan(version));
            Assert.That(block.MeasureInternal(Size.Infinity, resource), Is.EqualTo(size));
            Assert.That(after[0], Is.EqualTo(before[0]));
            Assert.That(after[1].Left, Is.GreaterThan(before[1].Left));
            Assert.That(after[1].Right, Is.EqualTo(after[0].Right).Within(1));
            Assert.That(after[1].Top, Is.EqualTo(before[1].Top));
        });
    }

    [Test]
    public void Alignment_RoundTripsAndOlderTextDefaultsToStart([Values] TextAlignment alignment)
    {
        TextBlock block = CreateTextBlock("Text");
        block.TextAlignment.Value = alignment;
        var json = CoreSerializer.SerializeToJsonObject(block);
        var restored = (TextBlock)CoreSerializer.DeserializeFromJsonObject(json, typeof(TextBlock));
        Assert.That(restored.TextAlignment.Value, Is.EqualTo(alignment));

        json.Remove(nameof(TextBlock.TextAlignment));
        var legacy = (TextBlock)CoreSerializer.DeserializeFromJsonObject(json, typeof(TextBlock));
        Assert.That(legacy.TextAlignment.Value, Is.EqualTo(TextAlignment.Start));
    }

    [Test]
    public void TextNode_ExposesAndEvaluatesTextAlignment()
    {
        var node = new TextNode();
        node.Object.Value.Text.Value = "HHHHHH\nHH";
        var input = node.Items.OfType<InputPort<TextAlignment>>().Single();
        input.Property!.SetValue(TextAlignment.Center);
        var graph = new GraphModel();
        graph.Nodes.Add(node);
        using var snapshot = new GraphSnapshot();
        snapshot.Build(graph, CompositionContext.Default);
        snapshot.Evaluate(CompositionContext.Default);
        var resource = (TextNode.Resource)snapshot.GetResource(snapshot.FindSlotIndex(node))!;

        Assert.That(node.Object.Value.TextAlignment.Value, Is.EqualTo(TextAlignment.Center));
        var textResource = (TextBlock.Resource)resource.Output!.Drawable!.Value.Resource;
        Assert.That(textResource.TextAlignment, Is.EqualTo(TextAlignment.Center));
    }

    [Test]
    public void VerticalColumns_AlignToTheLongestColumn([Values] TextAlignment alignment, [Values] bool split)
    {
        TextBlock block = CreateTextBlock("日日\n日日日日\n\n日<b>日</b>日", 40);
        block.WritingMode.Value = WritingMode.VerticalRl;
        block.TextAlignment.Value = alignment;
        block.SplitByCharacters.Value = split;
        using var resource = block.ToResource(CompositionContext.Default);
        using var node = new DrawableRenderNode(resource);
        Record(block, resource, node);
        Rect[] columns = RenderLineBounds(node, vertical: true);
        Assert.That(columns, Has.Length.EqualTo(3));
        // Pixel scanning visits the leftmost (last) column first.
        Assert.That(columns[1].Height, Is.GreaterThan(columns[2].Height));
        foreach (Rect column in columns)
        {
            float anchor = alignment switch
            {
                TextAlignment.Center => column.Center.Y,
                TextAlignment.End => column.Bottom,
                _ => column.Top,
            };
            float expected = alignment switch
            {
                TextAlignment.Center => columns[1].Center.Y,
                TextAlignment.End => columns[1].Bottom,
                _ => columns[1].Top,
            };
            Assert.That(anchor, Is.EqualTo(expected).Within(1));
        }

        block.WritingMode.Value = WritingMode.HorizontalTb;
        bool changed = false;
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        node.Update(resource);
        Record(block, resource, node);
        Assert.That(RenderLineBounds(node), Has.Length.EqualTo(3));
    }

    private static void Record(TextBlock block, TextBlock.Resource resource, DrawableRenderNode node)
    {
        using var context = new GraphicsContext2D(node, new Size(600, 400));
        block.Render(context, resource);
    }

    private static Rect[] RenderLineBounds(DrawableRenderNode node, bool vertical = false)
    {
        using RenderTarget target = RenderTarget.Create(600, 400)!;
        using (var canvas = new ImmediateCanvas(target, RenderIntent.Preview))
        {
            canvas.Clear();
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                CacheOptions = RenderCacheOptions.Disabled,
            });
            renderer.Render(canvas);
        }

        using Bitmap snapshot = target.Snapshot();
        using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
        var lines = new List<Rect>();
        Rect bounds = default;
        for (int outer = 0; outer < (vertical ? bitmap.Width : bitmap.Height); outer++)
        {
            bool hasInk = false;
            for (int inner = 0; inner < (vertical ? bitmap.Height : bitmap.Width); inner++)
            {
                int x = vertical ? outer : inner;
                int y = vertical ? inner : outer;
                if (bitmap.GetRow(y)[x * 4 + 3] < 128) continue;
                hasInk = true;
                bounds = bounds.Union(new Rect(x, y, 1, 1));
            }

            if (!hasInk && bounds != default)
            {
                lines.Add(bounds);
                bounds = default;
            }
        }
        if (bounds != default) lines.Add(bounds);
        return lines.ToArray();
    }

    private static TextBlock CreateTextBlock(string text, float size = 100)
    {
        Typeface typeface = TypefaceProvider.Typeface();
        var tb = new TextBlock();
        tb.FontFamily.Value = typeface.FontFamily;
        tb.FontStyle.Value = typeface.Style;
        tb.FontWeight.Value = typeface.Weight;
        tb.Size.Value = size;
        tb.Fill.Value = Brushes.Black;
        tb.Spacing.Value = 0;
        tb.Text.Value = text;
        return tb;
    }
}
