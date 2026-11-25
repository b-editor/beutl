using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

[NonParallelizable]
public sealed class TextAnimationUnitRenderingTests
{
    private static readonly Rect s_domain = new(0, 0, 288, 256);
    private static readonly RenderNodeRenderRequest s_request = new()
    {
        Intent = RenderIntent.Delivery,
        TargetDomain = s_domain,
    };

    [TestCase(TextAnimationUnit.Words, 3)]
    [TestCase(TextAnimationUnit.Lines, 5)]
    public void ARangeSelectsWholeWordsOrLines(TextAnimationUnit unit, int moved)
    {
        var before = new Probe();
        var after = new Probe();
        var range = new RangeAnimatorEffect
        {
            Unit = { Value = unit },
            RangeEnd = { Value = 28 },
            Smoothness = { Value = 0 },
            Position = { Value = new Point(0, 30) },
        };

        Render(CreateText("ABC de\nF gh", Chain(before.Effect, range, after.Effect)));

        Assert.That(before.Parts, Has.Length.EqualTo(8));
        Assert.That(YOffsets(before, after), Is.EqualTo(Enumerable.Range(0, 8).Select(i => i < moved ? 30f : 0f)).Within(0.001));
    }

    [TestCase(TextAnimationUnit.Characters)]
    [TestCase(TextAnimationUnit.Words)]
    [TestCase(TextAnimationUnit.Lines)]
    public void StrokeAndFillShareOneRangeSlotAndRotateAboutTheSameCenter(TextAnimationUnit unit)
    {
        var before = new Probe();
        var after = new Probe();
        var range = new RangeAnimatorEffect
        {
            Unit = { Value = unit },
            RangeEnd = { Value = unit == TextAnimationUnit.Characters ? 40 : 65 },
            Smoothness = { Value = 0 },
            Rotation = { Value = 180 },
        };
        TextBlock block = CreateText("   qA", Chain(before.Effect, range, after.Effect));
        block.Pen.Value = new Pen
        {
            Brush = { Value = Brushes.Red },
            Thickness = { Value = 5 },
            StrokeOrder = { Value = StrokeOrder.BelowFill },
            TrimStart = { Value = 30 },
            TrimEnd = { Value = 45 },
        };

        Render(block);

        Assert.That(before.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(new[] { 0, 1, 0, 1 }));
        Assert.That(before.Parts[0].Bounds.Center, Is.Not.EqualTo(before.Parts[2].Bounds.Center),
            "the trim must give the stroke its own bounds");
        Assert.That(after.Parts, Has.Length.EqualTo(4));
        for (int i = 0; i < after.Parts.Length; i++)
        {
            int character = before.Parts[i].Tag!.Value.Indices.Character;
            Rect union = before.Parts[character].Bounds.Union(before.Parts[character + 2].Bounds);
            bool selected = character == 0 || unit != TextAnimationUnit.Characters;
            Rect original = before.Parts[i].Bounds;
            Rect actual = after.Parts[i].Bounds;
            Assert.That(actual.X, Is.EqualTo(selected ? union.Left + union.Right - original.Right : original.X).Within(0.002),
                $"Part {i} started at {original} with character bounds {union} and ended at {actual}.");
            Assert.That(actual.Y, Is.EqualTo(selected ? union.Top + union.Bottom - original.Bottom : original.Y).Within(0.002));
            Assert.That(actual.Width, Is.EqualTo(original.Width).Within(0.002));
            Assert.That(actual.Height, Is.EqualTo(original.Height).Within(0.002));
        }
    }

    [Test]
    public void PartsSplitContoursKeepIndividualSlotsInLinesMode()
    {
        var before = new Probe();
        var after = new Probe();
        var range = new RangeAnimatorEffect
        {
            Unit = { Value = TextAnimationUnit.Lines },
            RangeEnd = { Value = 50 },
            Smoothness = { Value = 0 },
            Position = { Value = new Point(0, 24) },
        };
        TextBlock block = CreateText("i", Chain(new PartsSplitEffect(), before.Effect, range, after.Effect));
        block.SplitByCharacters.Value = false;

        Render(block);

        Assert.That(before.Parts, Has.Length.EqualTo(2));
        Assert.That(before.Parts.Select(part => part.Tag), Is.All.Null);
        Assert.That(YOffsets(before, after), Is.EqualTo(new[] { 24f, 0f }).Within(0.001));
    }

    [TestCase(DelayAnimationMode.PerStep, 0.18f)]
    [TestCase(DelayAnimationMode.Total, 0.06f)]
    public void DelayStartsEachWordsCharactersTogether(DelayAnimationMode mode, float step)
    {
        var probe = new Probe(readAlpha: true);
        DelayAnimationEffect delay = FadeDelay(180, mode);
        delay.Unit.Value = TextAnimationUnit.Words;

        Render(CreateText("ABC DE\nF GH", Chain(delay, probe.Effect)), 0.8);

        int[] words = [0, 0, 0, 1, 1, 2, 3, 3];
        Assert.That(probe.Parts.Select(part => part.Tag?.Indices.Word), Is.EqualTo(words));
        Assert.That(probe.Parts.Select(part => part.PeakAlpha),
            Is.EqualTo(words.Select(word => 0.8f - step * word)).Within(0.004));
    }

    [TestCase(WritingMode.HorizontalTb, DelayAnimationOrder.TopToBottom, "xh,\nZ")]
    [TestCase(WritingMode.VerticalRl, DelayAnimationOrder.RightToLeft, "Ai,\nZ")]
    public void PositionDelayStartsTheWholeLineOrColumnTogether(WritingMode writing, DelayAnimationOrder order, string content)
    {
        var probe = new Probe(readAlpha: true);
        DelayAnimationEffect delay = FadeDelay(400, DelayAnimationMode.Total);
        delay.Order.Value = order;
        TextBlock block = CreateText(content, Chain(delay, probe.Effect));
        block.WritingMode.Value = writing;

        Render(block, 0.7);

        Assert.That(probe.Parts.Select(part => part.Tag?.Indices.Line), Is.EqualTo(new[] { 0, 0, 0, 1 }));
        Assert.That(probe.Parts.Select(part => part.PeakAlpha), Is.EqualTo(new[] { 0.7f, 0.7f, 0.7f, 0.3f }).Within(0.004));
    }

    [Test]
    public void CroppingKeepsTheFullSelectionAndReadsFromOutsideTheCrop()
    {
        var range = new RangeAnimatorEffect
        {
            Unit = { Value = TextAnimationUnit.Words },
            RangeEnd = { Value = 28 },
            Position = { Value = new Point(130, 0) },
            Opacity = { Value = 60 },
            Smoothness = { Value = 0 },
        };
        using var text = CreateText("AB C\nD E", range).ToResource(AtTime(0));
        using var root = Record(text);
        using var renderer = CreateRenderer(root);
        var region = new Rect(120, 0, 68, 48);

        using var full = renderer.Rasterize();
        using var cropped = renderer.Rasterize(s_request with { RequestedRegion = region });

        Assert.That(cropped.Bounds, Is.EqualTo(region));
        var subset = PixelRect.FromRect(region.Translate(new Vector(-full.Bounds.X, -full.Bounds.Y)), 1);
        using Bitmap expected = full.Bitmap!.ExtractSubset(subset);
        Assert.That(expected.GetPixelSpan().ToArray(), Has.Some.Not.Zero, "the crop should show a word from outside its region");
        Assert.That(cropped.Bitmap!.GetPixelSpan().ToArray(), Is.EqualTo(expected.GetPixelSpan().ToArray()));
    }

    private static float[] YOffsets(Probe before, Probe after)
    {
        Assert.That(after.Parts, Has.Length.EqualTo(before.Parts.Length));
        return before.Parts.Zip(after.Parts, (input, output) => output.Bounds.Y - input.Bounds.Y).ToArray();
    }

    private static DelayAnimationEffect FadeDelay(float milliseconds, DelayAnimationMode mode)
    {
        var fade = new OpacityEffect();
        fade.Opacity.SetKeyFrameAnimation(TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1), useGlobalClock: true));
        return new DelayAnimationEffect { Delay = { Value = milliseconds }, DelayMode = { Value = mode }, Effect = { Value = fade } };
    }

    private static TextBlock CreateText(string content, FilterEffect effect) => new()
    {
        Text = { Value = content },
        FontFamily = { Value = TypefaceProvider.Typeface().FontFamily },
        Size = { Value = 36 },
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

    private static CompositionContext AtTime(double seconds) => new(MediaTime.FromSeconds(seconds)) { Fonts = TestFonts.Manager };

    private static void Render(TextBlock block, double seconds = 0)
    {
        using var resource = block.ToResource(AtTime(seconds));
        using var root = Record(resource);
        using var renderer = CreateRenderer(root);
        using var image = renderer.Rasterize();
        Assert.That(image.IsEmpty, Is.False);
    }

    private static ContainerRenderNode Record(Drawable.Resource resource)
    {
        var root = new ContainerRenderNode();
        using var context = new GraphicsContext2D(root, s_domain.Size);
        context.DrawDrawable(resource);
        return root;
    }

    private static RenderNodeRenderer CreateRenderer(RenderNode root)
        => new(root, s_request, new CpuRenderTargetFactory());

    private readonly record struct Part(TextPartTag? Tag, Rect Bounds, float PeakAlpha);

    private sealed class Probe
    {
        public Probe(bool readAlpha = false)
        {
            Effect = new EffectItemSuffixCallbackFilterEffect((context, _) =>
                context.CustomEffect((Probe: this, ReadAlpha: readAlpha), static (data, execution) =>
                    data.Probe.Parts = execution.Targets.Select(target => new Part(
                        target.TextPart, target.Bounds, data.ReadAlpha ? PeakAlpha(target) : 0)).ToArray()));
        }

        public FilterEffect Effect { get; }
        public Part[] Parts { get; private set; } = [];

        private static float PeakAlpha(EffectTarget target)
        {
            using Bitmap image = target.RenderTarget!.Snapshot();
            ReadOnlySpan<ushort> pixels = image.GetPixelSpan<ushort>();
            float peak = 0;
            for (int i = 3; i < pixels.Length; i += 4)
                peak = Math.Max(peak, (float)BitConverter.UInt16BitsToHalf(pixels[i]));
            return peak;
        }
    }
}
