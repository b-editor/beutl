using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
public sealed class TextPartTagTests
{
    private static readonly Rect s_domain = new(0, 0, 128, 96);
    private static readonly CompositionContext s_context = new(MediaTime.Zero) { Fonts = TestFonts.Manager };
    private static readonly RenderNodeRenderRequest s_request = new()
    {
        Intent = RenderIntent.Preview,
        TargetDomain = s_domain,
        Purpose = RenderRequestPurpose.Frame,
    };

    [OneTimeSetUp]
    public void RegisterFonts()
    {
        _ = TypefaceProvider.Typeface();
        using var stream = typeof(TextPartTagTests).Assembly.GetManifestResourceStream("NotoColorEmoji.ttf")!;
        TestFonts.Manager.AddFont(stream);
    }

    [Test]
    public void StrokeBelowFillAndFillCarryTheSameTagsInPaintOrder()
    {
        var probe = new TagProbe();
        TextBlock text = CreateText("Aq", probe.Effect);
        text.Pen.Value = new Pen
        {
            Brush = { Value = Brushes.Black },
            Thickness = { Value = 2 },
            StrokeOrder = { Value = StrokeOrder.BelowFill },
        };
        Render(text);

        TextPartTag[] tags = GetTextTags(probe);
        Assert.That(tags.Select(tag => tag.Indices.Character), Is.EqualTo(new[] { 0, 1, 0, 1 }));
        Assert.That(tags[..2], Is.EqualTo(tags[2..]));
        Assert.That(tags.Select(tag => tag.Counts), Is.All.EqualTo(new TextPartCounts(2, 1, 1)));
    }

    [Test]
    public void VerticalTextTagsOutlineAndColorGlyphsByColumn()
    {
        var (families, emoji) = TestFonts.Manager.GetFallbackFamilies();
        TestFonts.Manager.SetFallbackFonts([TypefaceProvider.Typeface().FontFamily], new FontFamily("Noto Color Emoji"));
        try
        {
            var probe = new TagProbe();
            TextBlock text = CreateText("A😀\nB", probe.Effect);
            text.WritingMode.Value = WritingMode.VerticalRl;
            Render(text);

            TextPartTag[] tags = GetTextTags(probe);
            Assert.That(tags.Select(tag => tag.Indices), Is.EqualTo(new TextPartIndices[]
            {
                new(0, 0, 0), new(1, 0, 0), new(2, 1, 1),
            }));
            Assert.That(tags.Select(tag => tag.WritingMode), Is.All.EqualTo(WritingMode.VerticalRl));
            Assert.That(tags.Select(tag => tag.Counts), Is.All.EqualTo(new TextPartCounts(3, 2, 2)));
        }
        finally
        {
            TestFonts.Manager.SetFallbackFonts(families, emoji);
        }
    }

    [Test]
    public void FontRevisionRefreshesTagsOnAnExistingTextResource()
    {
        var (families, emoji) = TestFonts.Manager.GetFallbackFamilies();
        TestFonts.Manager.SetFallbackFonts([new FontFamily("Beutl Test Color Emoji")], null);
        try
        {
            var probe = new TagProbe();
            TextBlock text = CreateText("👩‍💻", probe.Effect);
            text.FontFamily.Value = new FontFamily("Beutl Test Separate Emoji");
            using var resource = text.ToResource(s_context);
            using var root = RecordDrawable(resource);
            using var renderer = CreateRenderer(root);
            using var first = renderer.Rasterize();
            TextPartTag joined = GetTextTags(probe).Single();

            TestFonts.Manager.SetFallbackFonts([], null);
            using (var context = new GraphicsContext2D(root, s_domain.Size))
                context.DrawDrawable(resource);
            using var second = renderer.Rasterize();

            TextPartTag[] components = GetTextTags(probe);
            Assert.That(components.Length, Is.GreaterThan(1));
            Assert.That(components.Select(tag => tag.OccurrenceId), Is.All.EqualTo(joined.OccurrenceId));
            Assert.That(components.Select(tag => tag.Indices), Is.All.EqualTo(joined.Indices));
            Assert.That(components.Select(tag => tag.LayoutRevision), Is.All.GreaterThan(joined.LayoutRevision));
        }
        finally
        {
            TestFonts.Manager.SetFallbackFonts(families, emoji);
        }
    }

    [Test]
    public void TwoUsesOfTheSameResourceKeepDistinctStableOccurrencesBesideAShape()
    {
        var probe = new TagProbe();
        using var text = CreateText("A").ToResource(s_context);
        using var effect = probe.Effect.ToResource(s_context);
        using var root = new FilterEffectRenderNode(effect);
        using var renderer = CreateRenderer(root);

        Record();
        using var first = renderer.Rasterize();
        Assert.That(probe.Tags, Has.Length.EqualTo(3));
        Assert.That(probe.Tags[0]?.OccurrenceId, Is.GreaterThan(0));
        Assert.That(probe.Tags[1]?.OccurrenceId, Is.GreaterThan(0));
        Assert.That(probe.Tags[0]?.OccurrenceId, Is.Not.EqualTo(probe.Tags[1]?.OccurrenceId));
        Assert.That(probe.Tags[2], Is.Null);
        TextPartTag?[] tags = probe.Tags;

        Record();
        using var second = renderer.Rasterize();
        Assert.That(probe.Tags, Is.EqualTo(tags));

        void Record()
        {
            using var context = new GraphicsContext2D(root, s_domain.Size);
            context.DrawDrawable(text);
            using (context.PushTransform(Matrix.CreateTranslation(35, 0)))
                context.DrawDrawable(text);
            context.DrawRectangle(new Rect(70, 0, 12, 12), Brushes.Resource.White, null);
        }
    }

    [Test]
    public void PartsSplitContoursAreGenericParts()
    {
        var before = new TagProbe();
        var after = new TagProbe();
        Render(CreateText("i", Chain(before.Effect, new PartsSplitEffect(), after.Effect)));

        Assert.That(GetTextTags(before), Has.Length.EqualTo(1));
        Assert.That(after.Tags, Has.Length.EqualTo(2), "the dot and stem should produce separate contours");
        Assert.That(after.Tags, Is.All.Null);
    }

    [Test]
    public void FullAndCroppedRequestsGiveTheSameTextTags()
    {
        var probe = new TagProbe(samplesOutputRegion: true);
        using var text = CreateText("AB", probe.Effect).ToResource(s_context);
        using var root = RecordDrawable(text);
        using var renderer = CreateRenderer(root);

        using var full = renderer.Rasterize();
        TextPartTag[] tags = GetTextTags(probe);
        Assert.That(tags, Has.Length.EqualTo(2));
        Rect firstBounds = probe.Bounds[0];
        var cropped = new Rect(MathF.Ceiling(firstBounds.X), MathF.Ceiling(firstBounds.Y),
            MathF.Floor(firstBounds.Width / 2), MathF.Floor(firstBounds.Height / 2));
        using var partial = renderer.Rasterize(s_request with { RequestedRegion = cropped });

        Assert.That(partial.Bounds, Is.EqualTo(cropped));
        Assert.That(probe.Bounds[0].Width, Is.LessThan(firstBounds.Width));
        Assert.That(GetTextTags(probe), Is.EqualTo(tags));
    }

    [Test]
    public void RetainedValueRestoresTheSameTagOnACacheHit()
    {
        var probe = new TagProbe();
        using var effect = probe.Effect.ToResource(s_context);
        using var root = new FilterEffectRenderNode(effect);
        using var geometry = CreateGeometry();
        GeometryRenderNode leaf = RecordGeometry(root, geometry, SampleTag);
        using var renderer = CreateRenderer(root, RenderCacheOptions.Enabled);
        using var cold = renderer.Rasterize();
        Assert.That(GetTextTags(probe), Is.EqualTo(new[] { SampleTag }));

        leaf.Cache.RecordStableRequests();
        using var capture = renderer.Rasterize();
        Assert.That(leaf.Cache.IsCached, Is.True);
        int captures = renderer.LastExecutionStatistics.IntermediateTargetAcquisitions;
        using var warm = renderer.Rasterize();

        Assert.That(renderer.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.LessThan(captures));
        Assert.That(GetTextTags(probe), Is.EqualTo(new[] { SampleTag }));
        Assert.That(warm.Bitmap!.GetPixelSpan().ToArray(), Is.EqualTo(cold.Bitmap!.GetPixelSpan().ToArray()));
    }

    [Test]
    public void MetadataOnlyEditChangesNodeAndCacheIdentityWithIdenticalPixels()
    {
        var probe = new TagProbe();
        using var effect = probe.Effect.ToResource(s_context);
        using var root = new FilterEffectRenderNode(effect);
        using var geometry = CreateGeometry();
        GeometryRenderNode leaf = RecordGeometry(root, geometry, SampleTag);
        using var renderer = CreateRenderer(root, RenderCacheOptions.Enabled);
        using var initial = renderer.Rasterize();
        leaf.Cache.RecordStableRequests();
        using var cached = renderer.Rasterize();
        Assert.That(leaf.Cache.IsCached, Is.True);
        Assert.That(GetTextTags(probe), Is.EqualTo(new[] { SampleTag }));

        var editedTag = SampleTag with { LayoutRevision = 2, Indices = new TextPartIndices(1, 1, 0) };
        Assert.That(RecordGeometry(root, geometry, editedTag), Is.SameAs(leaf));
        Assert.That(leaf.HasChanges, Is.True);
        // A shared node's change flag may have been consumed by a different root.
        leaf.ClearChanges(leaf.ChangeVersion);
        root.ClearChanges(root.ChangeVersion);
        using var edited = renderer.Rasterize();

        Assert.That(GetTextTags(probe), Is.EqualTo(new[] { editedTag }));
        Assert.That(edited.Bitmap!.GetPixelSpan().ToArray(), Is.EqualTo(cached.Bitmap!.GetPixelSpan().ToArray()));
    }

    [TestCase("A", true)]
    [TestCase("AB", false)]
    public void TransformAndDelayOutputsKeepTagsUntilACompositeMixesCharacters(string content, bool sameCharacter)
    {
        var doubled = new TagProbe();
        var merged = new TagProbe();
        var transform = new TransformEffect { Transform = { Value = new TranslateTransform(4, 0) } };
        var duplicate = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            context.CustomEffect(0, static (_, execution) => execution.Targets.Add(execution.Targets[0].Clone())));
        var delay = new DelayAnimationEffect { Effect = { Value = Chain(transform, duplicate) } };
        Render(CreateText(content, Chain(delay, doubled.Effect, new LayerEffect(), merged.Effect)));

        TextPartTag[] tags = GetTextTags(doubled);
        Assert.That(tags.Select(tag => tag.Indices.Character), Is.EqualTo(sameCharacter ? new[] { 0, 0 } : new[] { 0, 0, 1, 1 }));
        Assert.That(merged.Tags, Is.EqualTo(new TextPartTag?[] { sameCharacter ? tags[0] : null }));
    }

    private static readonly TextPartTag SampleTag = new(
        100, 1, new TextPartIndices(0, 0, 0), new TextPartCounts(2, 2, 1), WritingMode.HorizontalTb);

    private static Geometry.Resource CreateGeometry()
        => new RectGeometry { Width = { Value = 32 }, Height = { Value = 24 } }.ToResource(s_context);

    private static GeometryRenderNode RecordGeometry(ContainerRenderNode root, Geometry.Resource geometry, TextPartTag tag)
    {
        using var context = new GraphicsContext2D(root, s_domain.Size);
        context.DrawGeometry(geometry, Brushes.Resource.White, null, tag);
        return (GeometryRenderNode)root.Children.Single();
    }

    private static TextBlock CreateText(string content, FilterEffect? effect = null) => new()
    {
        Text = { Value = content },
        FontFamily = { Value = TypefaceProvider.Typeface().FontFamily },
        Size = { Value = 26 },
        SplitByCharacters = { Value = true },
        AlignmentX = { Value = AlignmentX.Left },
        AlignmentY = { Value = AlignmentY.Top },
        FilterEffect = { Value = effect },
    };

    private static FilterEffectGroup Chain(params FilterEffect[] effects)
    {
        var group = new FilterEffectGroup();
        foreach (FilterEffect effect in effects)
            group.Children.Add(effect);
        return group;
    }

    private static void Render(TextBlock text)
    {
        using var resource = text.ToResource(s_context);
        using var root = RecordDrawable(resource);
        using var renderer = CreateRenderer(root);
        using var image = renderer.Rasterize();
        Assert.That(image.IsEmpty, Is.False);
    }

    private static ContainerRenderNode RecordDrawable(Drawable.Resource resource)
    {
        var root = new ContainerRenderNode();
        using var context = new GraphicsContext2D(root, s_domain.Size);
        context.DrawDrawable(resource);
        return root;
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root, RenderCacheOptions? cache = null)
        => new(root, s_request with { CacheOptions = cache ?? RenderCacheOptions.Disabled }, new CpuRenderTargetFactory());

    private static TextPartTag[] GetTextTags(TagProbe probe)
    {
        Assert.That(probe.Tags, Is.Not.Empty);
        Assert.That(probe.Tags, Is.All.Not.Null);
        return probe.Tags.Select(tag => tag!.Value).ToArray();
    }

    private sealed class TagProbe
    {
        public TagProbe(bool samplesOutputRegion = false)
        {
            Effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
            {
                if (samplesOutputRegion)
                    context.CustomEffectWithSamplingBounds(this, Capture, static (_, output) => output);
                else
                    context.CustomEffect(this, Capture);
            });
        }

        public FilterEffect Effect { get; }

        public TextPartTag?[] Tags { get; private set; } = [];

        public Rect[] Bounds { get; private set; } = [];

        private static void Capture(TagProbe probe, CustomFilterEffectContext context)
        {
            probe.Tags = context.Targets.Select(static target => target.TextPart).ToArray();
            probe.Bounds = context.Targets.Select(static target => target.Bounds).ToArray();
        }
    }
}
