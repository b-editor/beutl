using System.Collections;
using System.Reflection;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Serialization;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine;

[NonParallelizable]
public class VerticalTextTests
{
    private FontFamily[] _previousFamilies = [];
    private FontFamily? _previousEmoji;

    [OneTimeSetUp]
    public void RegisterFonts()
    {
        _ = TypefaceProvider.Typeface();
        using var stream = typeof(VerticalTextTests).Assembly.GetManifestResourceStream("NotoColorEmoji.ttf")!;
        FontManager.Instance.AddFont(stream);
    }

    [SetUp]
    public void SetFallbacks()
    {
        (_previousFamilies, _previousEmoji) = FontManager.Instance.GetFallbackFamilies();
        FontManager.Instance.SetFallbackFonts([TypefaceProvider.Typeface().FontFamily], new FontFamily("Noto Color Emoji"));
    }

    [TearDown]
    public void RestoreFallbacks() => FontManager.Instance.SetFallbackFonts(_previousFamilies, _previousEmoji);

    private static TextBlock Create(string text) => new()
    {
        Text = { Value = text },
        FontFamily = { Value = TypefaceProvider.Typeface().FontFamily },
        Size = { Value = 40 },
        WritingMode = { Value = WritingMode.VerticalRl },
    };

    [Test]
    public void Columns_PreserveEmptyLinesAndRotateLatinRuns()
    {
        using var resource = Create("日本ABC123。\n\n語\n").ToResource(CompositionContext.Default);
        VerticalTextLayout layout = resource.GetVerticalLayout();
        Assert.That(layout.Columns, Has.Count.EqualTo(4));
        Assert.That(layout.Columns[1].Runs, Is.Empty);
        Assert.That(layout.Columns[3].Runs, Is.Empty);
        Assert.That(layout.Columns[0].Runs.Select(r => (r.Text.Text.ToString(), r.Sideways)),
            Is.EqualTo(new[] { ("日本", false), ("ABC123", true), ("。", false) }));
        Assert.That(layout.Columns[0].Height, Is.GreaterThan(layout.Columns[2].Height));
        Assert.That(layout.Size.Width, Is.EqualTo(layout.Columns.Sum(c => c.Width)));
    }

    [TestCase("1\uFE0F\u20E3", false)]
    [TestCase("A\u20DD", false)]
    [TestCase("12", true)]
    public void ClusterOrientation_UsesEnclosingMarksAndKeepsOrdinaryDigitsSideways(string cluster, bool sideways)
    {
        using var resource = Create(cluster).ToResource(CompositionContext.Default);
        var run = resource.GetVerticalLayout().Columns.Single().Runs.Single();
        Assert.That(run.Sideways, Is.EqualTo(sideways));
    }

    [Test]
    public void SidewaysSpacing_AdvancesWholeCombiningClusters()
    {
        TextBlock block = Create("x\u0301x\u0301");
        using var resource = block.ToResource(CompositionContext.Default);
        float before = resource.GetVerticalLayout().Size.Height;
        Rect[] unspaced = resource.GetVerticalLayout().Columns.Single().Runs.Single().Text.ToGeometries()
            .ToArray().Select(glyph => glyph.Bounds).ToArray();
        block.Spacing.Value = 10;
        bool changed = false;
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        var layout = resource.GetVerticalLayout();
        Assert.That(layout.Columns.Single().Runs.Single().Sideways, Is.True);
        Assert.That(layout.Size.Height - before, Is.EqualTo(20).Within(0.01),
            "two grapheme clusters have two spacing cells; their combining marks must not add spacing");
        var spaced = layout.Columns.Single().Runs.Single().Text.ToGeometries();
        Assert.That(spaced.Length, Is.EqualTo(4), "each x and its accent must be separate glyphs in this fixture");
        for (int i = 0; i < spaced.Length; i++)
        {
            Assert.That(spaced[i].Bounds.X - unspaced[i].X, Is.EqualTo(i < 2 ? 0 : 10).Within(0.01),
                "the base and its accent must move together");
        }
    }

    [TestCase("fi")]
    [TestCase("ffi")]
    public void SidewaysSpacing_SeparatesOptionalLigaturesOnlyWhenSpaced(string content)
    {
        using var text = TypefaceProvider.CreateText(content, 40);
        text.Font = new FontFamily("Roboto");
        text.Orientation = TextRunOrientation.Sideways;
        int unspacedGlyphs = text.ToGeometries().Length;
        Assert.That(unspacedGlyphs, Is.LessThan(content.Length), "the fixture must form a ligature");

        text.Spacing = 10;
        float width = text.Bounds.Width;
        Assert.That(text.ToGeometries().Length, Is.EqualTo(content.Length));
        text.Spacing = 20;
        Assert.That(text.Bounds.Width - width, Is.EqualTo((content.Length - 1) * 10).Within(0.01));

        text.Spacing = 0;
        Assert.That(text.ToGeometries().Length, Is.EqualTo(unspacedGlyphs));
        text.Orientation = TextRunOrientation.Horizontal;
        text.Spacing = 10;
        Assert.That(text.ToGeometries().Length, Is.EqualTo(unspacedGlyphs), "retain existing horizontal shaping");
    }

    [TestCase("👩‍💻👩‍💻", false)]
    [TestCase("א👩‍💻", true)]
    public void VerticalSpacing_CountsGraphemesWhenEmojiUseSeparateGlyphs(string content, bool sideways)
    {
        FontManager.Instance.SetFallbackFonts([], null);
        using var text = TypefaceProvider.CreateText(content, 40);
        text.Font = new FontFamily("Beutl Test Separate Emoji");
        text.Orientation = sideways ? TextRunOrientation.Sideways : TextRunOrientation.Upright;
        Assert.That(text.ToGeometries().Length, Is.GreaterThan(2), "the fixture must have separate component glyphs");
        float before = sideways ? text.Bounds.Width : text.Bounds.Height;
        text.Spacing = 10;
        float after = sideways ? text.Bounds.Width : text.Bounds.Height;
        Assert.That(after - before, Is.EqualTo(10).Within(0.01), "two graphemes have only one internal spacing gap");
    }

    [Test]
    public void VerticalAlternates_AreReusedForTypographyAndInvalidatedForFontChanges()
    {
        TextBlock block = Create("ーーー");
        block.FontFamily.Value = new FontFamily("Roboto");
        using var resource = block.ToResource(CompositionContext.Default);
        Assert.That(resource.GetVerticalLayout().Columns.Single().Runs.Single().Sideways, Is.False,
            "the Japanese fallback supplies a vertical alternate");
        var cache = (IDictionary)typeof(TextBlock.Resource)
            .GetField("_verticalAlternates", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(resource)!;
        Assert.That(cache.Count, Is.EqualTo(1), "repeated punctuation shares the resolved typeface/cluster entry");

        block.Size.Value = 60;
        block.Spacing.Value = 5;
        bool changed = false;
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        Assert.That(cache.Count, Is.EqualTo(1), "size and spacing do not invalidate alternate selection");
        Assert.That(resource.GetVerticalLayout().Columns.Single().Runs.Single().Sideways, Is.False);
        Assert.That(cache.Count, Is.EqualTo(1));

        block.Text.Value = "ーー";
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        Assert.That(cache.Count, Is.EqualTo(1), "text edits keep the typeface/cluster entries");
        Assert.That(resource.GetVerticalLayout().Columns.Single().Runs.Single().Sideways, Is.False);
        Assert.That(cache.Count, Is.EqualTo(1));

        FontManager.Instance.SetFallbackFonts([], null);
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        Assert.That(cache.Count, Is.Zero, "font revisions invalidate cached selections");
        Assert.That(resource.GetVerticalLayout().Columns.Single().Runs.Single().Sideways, Is.True,
            "Roboto alone has no vertical alternate for this character");
        Assert.That(cache.Count, Is.EqualTo(1), "negative results are cached too");

        block.FontFamily.Value = TypefaceProvider.Typeface().FontFamily;
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        Assert.That(cache.Count, Is.Zero);
        Assert.That(resource.GetVerticalLayout().Columns.Single().Runs.Single().Sideways, Is.False);
    }

    [TestCase("日\n\n日\n")]
    [TestCase("<size=20>日\r\n\r\n日\r\n</size>")]
    public void BlankColumns_UseTheSameFontMetricsAsOccupiedColumns(string content)
    {
        using var resource = Create(content).ToResource(CompositionContext.Default);
        var columns = resource.GetVerticalLayout().Columns;
        Assert.That(columns, Has.Count.EqualTo(4));
        Assert.That(columns.Select(column => column.Width), Is.All.EqualTo(columns[0].Width).Within(0.01));
    }

    [Test]
    public void VerticalShaping_UsesDownwardAdvancesAndVerticalAlternates()
    {
        using var text = TypefaceProvider.CreateText("「あー。」", 40);
        using var font = text.ToSKFont();
        using var shaper = new TextShaper(font.Typeface);
        using var horizontal = new HarfBuzzSharp.Buffer();
        horizontal.AddUtf16(text.Text.AsSpan());
        horizontal.GuessSegmentProperties();
        var h = shaper.ShapeWithAdvance(horizontal, font);
        using var vertical = new HarfBuzzSharp.Buffer();
        vertical.AddUtf16(text.Text.AsSpan());
        vertical.GuessSegmentProperties();
        vertical.Direction = HarfBuzzSharp.Direction.TopToBottom;
        var v = shaper.ShapeWithAdvance(vertical, font);
        Assert.That(v.Advance.X, Is.Zero);
        Assert.That(v.Advance.Y, Is.EqualTo(200).Within(0.1));
        Assert.That(v.Points[^1].Y, Is.GreaterThan(v.Points[0].Y));
        Assert.That(v.Codepoints, Is.Not.EqualTo(h.Codepoints));
    }

    [Test]
    public void ChangingDirectionAndSize_InvalidatesMeasuredAndScaledText()
    {
        using var text = TypefaceProvider.CreateText("日本語", 40);
        var horizontal = text.GetTextBlob(2)!;
        text.Orientation = TextRunOrientation.Upright;
        var vertical = text.GetTextBlob(2)!;
        Assert.That(vertical, Is.Not.SameAs(horizontal));
        Assert.That(text.Bounds.Height, Is.EqualTo(120).Within(0.1));
        Assert.That(vertical.Bounds.Height, Is.GreaterThan(vertical.Bounds.Width));
        text.Size = 60;
        Assert.That(text.Bounds.Height, Is.EqualTo(180).Within(0.1));
        text.Orientation = TextRunOrientation.Horizontal;
        Assert.That(text.Bounds.Width, Is.EqualTo(180).Within(0.1));
    }

    [Test]
    public void VerticalFallbackGlyphs_PreservePositionAndRasterBoundsWhenSplit()
    {
        using var text = TypefaceProvider.CreateText("日😀本", 40, 3);
        text.Orientation = TextRunOrientation.Upright;
        var geometries = text.ToGeometries();
        Assert.That(geometries.Length, Is.EqualTo(3));
        var emoji = text.GetNonOutlineGlyph(1);
        Assert.That(emoji, Is.Not.Null);
        Assert.That(emoji!.ActualBounds.Top, Is.GreaterThan(geometries[0].Bounds.Bottom));
        Assert.That(geometries[2].Bounds.Top, Is.GreaterThan(emoji.ActualBounds.Top));
        Assert.That(text.RasterBounds.Contains(emoji.ActualBounds), Is.True);
        Assert.That(emoji.GetTextBlob(2)!.Bounds.Top, Is.EqualTo(emoji.GetTextBlob()!.Bounds.Top * 2).Within(2));
    }

    [Test]
    public void Layout_RebuildsForStyleChangesAndDisposesOwnedRuns()
    {
        TextBlock block = Create("日<size=60>本</size>ABCか\u3099👩‍💻");
        using var resource = block.ToResource(CompositionContext.Default);
        var old = resource.GetVerticalLayout();
        var text = old.Columns[0].Runs[0].Text;
        Assert.That(old.Columns[0].Width, Is.GreaterThanOrEqualTo(60));
        Assert.That(string.Concat(old.Columns[0].Runs.Select(r => r.Text.Text.ToString())), Is.EqualTo("日本ABCか\u3099👩‍💻"));
        block.Spacing.Value = 5;
        bool changed = false;
        resource.Reconcile(block, CompositionContext.Default, ref changed);
        Assert.That(text.IsDisposed, Is.True);
        Assert.That(resource.GetVerticalLayout(), Is.Not.SameAs(old));
    }

    [Test]
    public void WritingMode_RoundTripsDefaultsAndHasNodeInput()
    {
        var json = CoreSerializer.SerializeToJsonObject(Create("日本語"));
        var restored = (TextBlock)CoreSerializer.DeserializeFromJsonObject(json, typeof(TextBlock));
        Assert.That(restored.WritingMode.Value, Is.EqualTo(WritingMode.VerticalRl));
        json.Remove(nameof(TextBlock.WritingMode));
        var legacy = (TextBlock)CoreSerializer.DeserializeFromJsonObject(json, typeof(TextBlock));
        Assert.That(legacy.WritingMode.Value, Is.EqualTo(WritingMode.HorizontalTb));
        var node = new TextNode();
        node.Items.OfType<InputPort<WritingMode>>().Single().Property!.SetValue(WritingMode.VerticalRl);
        Assert.That(node.Object.Value.WritingMode.Value, Is.EqualTo(WritingMode.VerticalRl));
    }

    [TestCase(1f)]
    [TestCase(2f)]
    public void RenderMixedVerticalText(float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var block = Create("「日本語」の縦書き。\n文字とABC123、長ー音。\n<size=55>大きな</size><#e04040>文字</#>\n\n<cspace=10>x\u0301x\u0301</cspace>・1\uFE0F\u20E3😀\n<font='Roboto'><cspace=10>ffi</cspace></font>");
            block.Fill.Value = Brushes.Black;
            block.Pen.Value = new Pen { Brush = { Value = Brushes.Black }, Thickness = { Value = 1 } };
            using var resource = block.ToResource(CompositionContext.Default);
            using Bitmap grouped = GoldenImageHarness.RenderAtScale(resource, new PixelSize(650, 650), scale, clearColor: Colors.White);
            block.SplitByCharacters.Value = true;
            bool changed = false;
            resource.Reconcile(block, CompositionContext.Default, ref changed);
            using Bitmap split = GoldenImageHarness.RenderAtScale(resource, new PixelSize(650, 650), scale, clearColor: Colors.White);

            Assert.That(ImageMetrics.MeanAbsoluteError(grouped, split), Is.LessThan(0.002),
                "grouped and split rendering must preserve the same glyph placement, colors and strokes");
            using Bitmap pixels = grouped.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            Assert.That(pixels.SKBitmap.Pixels.Count(p => p.Red < 128), Is.GreaterThan(1000));
            if (Environment.GetEnvironmentVariable("BEUTL_VERTICAL_CAPTURE_DIR") is { } directory)
            {
                using var groupedOutput = File.Create(Path.Combine(directory, $"vertical-grouped-{scale}.png"));
                Assert.That(grouped.Save(groupedOutput, EncodedImageFormat.Png), Is.True);
                using var splitOutput = File.Create(Path.Combine(directory, $"vertical-split-{scale}.png"));
                Assert.That(split.Save(splitOutput, EncodedImageFormat.Png), Is.True);
            }
        });
    }
}
