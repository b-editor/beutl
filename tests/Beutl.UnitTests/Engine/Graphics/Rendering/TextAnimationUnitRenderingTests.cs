using Beutl.Animation;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Serialization;
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

    [Test]
    public void TextOnAStraightPathKeepsAdvanceSpacingAndBothPaintPasses()
    {
        var before = new Probe();
        var after = new Probe();
        TextOnPathEffect path = AlongLine(180);
        path.PathOffset.Value = 8;
        TextBlock block = CreateText("  x\u0301A V", Chain(before.Effect, path, after.Effect));
        block.Spacing.Value = 3;
        block.Pen.Value = new Pen
        {
            Brush = { Value = Brushes.Red },
            Thickness = { Value = 3 },
            StrokeOrder = { Value = StrokeOrder.BelowFill },
        };

        Render(block);

        var characters = new[] { 0, 0, 1, 2, 0, 0, 1, 2 };
        Assert.That(before.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(characters));
        Assert.That(after.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(characters));
        float baseline = before.Parts[0].Tag!.Value.Positions!.Characters[0].Anchor.Y;
        for (int i = 0; i < characters.Length; i++)
            AssertBounds(after.Parts[i].Bounds, before.Parts[i].Bounds.Translate(new Vector(48, 80 - baseline)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TextOnACurveCanFollowTheTangentOrStayUpright(bool followRotation)
    {
        var before = new Probe();
        var after = new Probe();
        var path = new TextOnPathEffect
        {
            Path = { Value = PathGeometry.Parse("M80 60 C80 120 220 120 220 160") },
            FollowRotation = { Value = followRotation },
        };
        TextBlock block = CreateText("W", Chain(before.Effect, path, after.Effect));
        using var resource = block.ToResource(AtTime(0));
        var placement = resource.GetTextPartLayout().Positions.Characters[0];
        path.PathOffset.Value = -placement.Distance;

        Render(block);

        // The initial control point is directly below the start, so the tangent is +Y.
        Matrix expected = Matrix.CreateTranslation(-placement.Anchor.X, -placement.Anchor.Y)
            * Matrix.CreateRotation(followRotation ? MathF.PI / 2 : 0) * Matrix.CreateTranslation(80, 60);
        AssertBounds(after.Parts.Single().Bounds, before.Parts.Single().Bounds.TransformToAABB(expected));
    }

    [TestCase(TextAnimationUnit.Characters, DelayAnimationOrder.Forward, 25)]
    [TestCase(TextAnimationUnit.Words, DelayAnimationOrder.Reverse, 50)]
    public void RangeSelectionKeepsItsSourceSlotWhenOffsetMovesTextPastAnEndpoint(
        TextAnimationUnit unit, DelayAnimationOrder order, float start)
    {
        TextBlock block = CreateText("A A A A", new FilterEffectGroup());
        using var resource = block.ToResource(AtTime(0));
        var characters = resource.GetTextPartLayout().Positions.Characters;
        TextOnPathEffect path = AlongLine((characters[1].Distance + characters[2].Distance) / 2);
        float shift = characters[0].Distance - characters[1].Distance;
        path.PathOffset.SetKeyFrameAnimation(TestAnimations.Linear(0f, shift, MediaTime.FromSeconds(1), useGlobalClock: true));
        var before = new Probe();
        var after = new Probe();
        var range = new RangeAnimatorEffect
        {
            Unit = { Value = unit },
            Order = { Value = order },
            RangeStart = { Value = start },
            RangeEnd = { Value = start + 25 },
            Smoothness = { Value = 0 },
            Position = { Value = new Point(0, 24) },
        };
        block.FilterEffect.Value = Chain(path, before.Effect, range, after.Effect);

        Render(block);
        Assert.That(before.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(new[] { 0, 1 }));
        Assert.That(YOffsets(before, after), Is.EqualTo(new[] { 0f, 24f }).Within(0.001));

        Render(block, 1);
        Assert.That(before.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(YOffsets(before, after), Is.EqualTo(new[] { 24f, 0f }).Within(0.001));
    }

    [TestCase(DelayAnimationMode.PerStep, 180f)]
    [TestCase(DelayAnimationMode.Total, 540f)]
    public void DelayTimingUsesOriginalCharacterSlotsWhenTextCrossesPathEndpoints(
        DelayAnimationMode mode, float milliseconds)
    {
        TextBlock block = CreateText("A A A A", new FilterEffectGroup());
        using var resource = block.ToResource(AtTime(0));
        var characters = resource.GetTextPartLayout().Positions.Characters;
        float advance = characters[1].Distance - characters[0].Distance;
        TextOnPathEffect path = AlongLine(characters[^1].Distance + advance / 2);
        var probe = new Probe(readAlpha: true);
        block.FilterEffect.Value = Chain(path, FadeDelay(milliseconds, mode), probe.Effect);

        (float Offset, int[] VisibleCharacters)[] samples =
        [
            (0, [0, 1, 2, 3]),
            (advance, [0, 1, 2]),
            (-advance, [1, 2, 3]),
        ];
        foreach (var (offset, visibleCharacters) in samples)
        {
            path.PathOffset.Value = offset;
            Render(block, 0.8);

            Assert.That(probe.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(visibleCharacters), $"Offset {offset} px");
            // 180 ms per step across the original four characters, even when the last one is hidden.
            Assert.That(probe.Parts.Select(part => part.PeakAlpha),
                Is.EqualTo(visibleCharacters.Select(character => 0.8f - 0.18f * character)).Within(0.004), $"Offset {offset} px");
        }
    }

    [TestCase(TextAnimationUnit.Characters, 0.6f, 0.5f)]
    [TestCase(TextAnimationUnit.Words, 0.7f, 0.7f)]
    [TestCase(TextAnimationUnit.Lines, 0.7f, 0.7f)]
    public void GroupDelayKeepsRanksWhenAWholeTextBlockLeavesAndReentersItsPath(
        TextAnimationUnit unit, float alphaC, float alphaD)
    {
        var probe = new Probe(readAlpha: true);
        DelayAnimationEffect delay = FadeDelay(100, DelayAnimationMode.PerStep);
        delay.Unit.Value = unit;
        TextOnPathEffect path = AlongLine(120);
        DrawableGroup group = CreatePathGroup(path, Chain(delay, probe.Effect));

        foreach (float offset in new[] { 0f, 121f, 0f })
        {
            path.PathOffset.Value = offset;
            Render(group, 0.8);

            Assert.That(probe.Parts, Has.Length.EqualTo(offset == 0 ? 4 : 2));
            Assert.That(probe.Parts[^2..].Select(part => part.PeakAlpha),
                Is.EqualTo(new[] { alphaC, alphaD }).Within(0.004), $"Offset {offset} px");
        }
    }

    [Test]
    public void GroupDelayKeepsTheAntialiasedBottomRowsOfTheRemainingGlyphs()
    {
        TextOnPathEffect path = AlongLine(120);
        path.PathOffset.Value = 121;
        DrawableGroup group = CreatePathGroup(path, new FilterEffectGroup());
        var sibling = (TextBlock)group.Children[1];
        sibling.Size.Value = 152;
        sibling.FontWeight.Value = FontWeight.Bold;
        sibling.AlignmentX.Value = AlignmentX.Center;
        sibling.Transform.Value = new TranslateTransform(0, 53);

        int[] reference = RenderBottomRows();
        group.FilterEffect.Value = FadeDelay(150, DelayAnimationMode.PerStep);

        Assert.That(RenderBottomRows(), Is.EqualTo(reference));

        int[] RenderBottomRows()
        {
            using var resource = group.ToResource(AtTime(0.8));
            using var root = Record(resource);
            using var renderer = CreateRenderer(root);
            using var target = new CpuRenderTarget((int)s_domain.Width, (int)s_domain.Height);
            using var canvas = new ImmediateCanvas(target, RenderIntent.Delivery, 1, logicalSize: s_domain.Size);
            canvas.Clear(Colors.Transparent);
            renderer.Render(canvas);
            using Bitmap image = target.Snapshot();
            using Bitmap alpha = image.Convert(BitmapColorType.Alpha8);

            int middle = alpha.Width / 2;
            return [LastInkRow(0, middle), LastInkRow(middle, alpha.Width - middle)];

            int LastInkRow(int x, int width) => Enumerable.Range(0, alpha.Height).Last(y =>
                alpha.GetPixelSpan().Slice(y * alpha.RowBytes + x, width).ContainsAnyExcept((byte)0));
        }
    }

    [TestCase(TextAnimationUnit.Characters, true)]
    [TestCase(TextAnimationUnit.Characters, false)]
    [TestCase(TextAnimationUnit.Words, true)]
    [TestCase(TextAnimationUnit.Lines, true)]
    public void GroupRangeKeepsItsSelectionWhenAWholeTextBlockLeavesAndReentersItsPath(TextAnimationUnit unit, bool pathFirst)
    {
        var before = new Probe();
        var after = new Probe();
        var range = new RangeAnimatorEffect
        {
            Unit = { Value = unit },
            RangeStart = { Value = pathFirst ? 0 : 50 },
            RangeEnd = { Value = pathFirst ? 50 : 100 },
            Smoothness = { Value = 0 },
            Position = { Value = new Point(0, 24) },
        };
        TextOnPathEffect path = AlongLine(120);
        DrawableGroup group = CreatePathGroup(path, Chain(before.Effect, range, after.Effect), pathFirst);
        float[] visible = pathFirst ? [24f, 24f, 0f, 0f] : [0f, 0f, 24f, 24f];

        foreach (float offset in new[] { 0f, 121f, 0f })
        {
            path.PathOffset.Value = offset;
            Render(group);

            float[] expected = offset == 0 ? visible : [0f, 0f];
            Assert.That(YOffsets(before, after), Is.EqualTo(expected).Within(0.001), $"Offset {offset} px");
        }
    }

    [Test]
    public void CachedGlyphsKeepTheirRanksWhenTheirTextBlockIsClipped()
    {
        TextOnPathEffect path = AlongLine(120);
        path.PathOffset.Value = 121;
        var probe = new Probe(readAlpha: true);
        CompositionContext time = AtTime(0.8);
        using var onPath = CreateText("AB").ToResource(time);
        using var sibling = CreateText("CD").ToResource(time);
        using var pathResource = path.ToResource(time);
        using var parentEffect = Chain(FadeDelay(100, DelayAnimationMode.PerStep), probe.Effect).ToResource(time);
        using var root = new FilterEffectRenderNode(parentEffect);
        var transform = new TransformRenderNode(Matrix.Identity, TransformOperator.Prepend);
        root.AddChild(transform);
        var pathNode = new FilterEffectRenderNode(pathResource);
        transform.AddChild(pathNode);
        root.AddChild(Record(sibling));

        // Record real glyphs on the same device grid so they are eligible for retention.
        TextPartLayout layout = onPath.GetTextPartLayout();
        TextPartLayout.Run run = layout.Runs.Single();
        using (var graphics = new GraphicsContext2D(pathNode, s_domain.Size))
        {
            ReadOnlySpan<Geometry.Resource> geometry = run.Text.ToGeometries();
            for (int i = 0; i < geometry.Length; i++)
                graphics.DrawGeometry(geometry[i], Brushes.Resource.White, null, layout.CreateTag(long.MaxValue, run.Glyphs[i]));
        }

        var request = s_request with
        {
            Intent = RenderIntent.Preview,
            Purpose = RenderRequestPurpose.Frame,
            CacheOptions = RenderCacheOptions.Enabled,
        };
        using var renderer = new RenderNodeRenderer(root, request, new CpuRenderTargetFactory());
        GeometryRenderNode[] glyphs = pathNode.Children.Cast<GeometryRenderNode>().ToArray();

        using var cold = renderer.Rasterize();
        Assert.That(probe.Parts.Select(part => part.PeakAlpha), Is.EqualTo(new[] { 0.6f, 0.5f }).Within(0.004));

        Assert.That(glyphs, Has.Length.EqualTo(2));
        foreach (GeometryRenderNode glyph in glyphs)
            glyph.Cache.RecordStableRequests();
        using var captured = renderer.Rasterize();
        Assert.That(glyphs.Select(glyph => glyph.Cache.IsCached), Is.All.True);
        int captureAcquisitions = renderer.LastExecutionStatistics.IntermediateTargetAcquisitions;
        using var warm = renderer.Rasterize();

        Assert.That(renderer.LastExecutionStatistics.IntermediateTargetAcquisitions, Is.LessThan(captureAcquisitions));
        Assert.That(probe.Parts.Select(part => part.PeakAlpha), Is.EqualTo(new[] { 0.6f, 0.5f }).Within(0.004));
        Assert.That(warm.Bitmap!.GetPixelSpan().ToArray(), Is.EqualTo(cold.Bitmap!.GetPixelSpan().ToArray()));
    }

    [Test]
    public void AClosedPathShowsOnlyTheFirstLap()
    {
        var after = new Probe();
        // The perimeter is 80 px. A center before 80 belongs to this lap, the next one does not.
        var path = new TextOnPathEffect
        {
            Path = { Value = PathGeometry.Parse("M80 80 L100 80 L100 100 L80 100 Z") },
            PathOffset = { Value = 56 },
        };

        Render(CreateText("AA", Chain(path, after.Effect)));

        Assert.That(after.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void OffsetCanBringAGlyphIntoViewFromOutsideTheFrame()
    {
        var after = new Probe();
        TextOnPathEffect path = AlongLine(100);
        TextBlock block = CreateText(new string(' ', 60) + "A", Chain(path, after.Effect));
        using var resource = block.ToResource(AtTime(0));
        var placement = resource.GetTextPartLayout().Positions.Characters[0];
        Assert.That(placement.Anchor.X, Is.GreaterThan(s_domain.Right));
        path.PathOffset.Value = -placement.Distance;

        Render(block);

        Assert.That(after.Parts.Single().Bounds.Center.X, Is.InRange(28, 52));
    }

    [Test]
    public void TextOnPathKeepsUnsplitTextTogether()
    {
        var before = new Probe();
        var after = new Probe();
        TextBlock block = CreateText("AB", Chain(before.Effect, AlongLine(100), after.Effect));
        block.SplitByCharacters.Value = false;

        Render(block);

        Assert.That(after.Parts, Is.EqualTo(before.Parts));
        Assert.That(after.Parts, Has.Length.EqualTo(1));
    }

    [Test]
    public void ANewEffectHasItsOwnPathAndDisplaysSplitText()
    {
        var effect = new TextOnPathEffect();
        var probe = new Probe();
        Assert.That(effect.Path.Value, Is.TypeOf<PathGeometry>());
        Assert.That(new TextOnPathEffect().Path.Value, Is.Not.SameAs(effect.Path.Value));

        Render(CreateText("AB", Chain(effect, probe.Effect)));

        Assert.That(probe.Parts.Select(part => part.Tag?.Indices.Character), Is.EqualTo(new[] { 0, 1 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AnUnassignedPathPassesThroughAndAnEmptyContourHidesSplitText(bool assignEmptyGeometry)
    {
        var path = new TextOnPathEffect { Path = { Value = assignEmptyGeometry ? new PathGeometry() : null } };

        using var text = CreateText("AB", path).ToResource(AtTime(0));
        using var root = Record(text);
        using var renderer = CreateRenderer(root);
        using var image = renderer.Rasterize();
        bool hasInk = false;
        if (image.Bitmap is { } bitmap)
        {
            using Bitmap alpha = bitmap.Convert(BitmapColorType.Alpha8);
            hasInk = alpha.GetPixelSpan().ContainsAnyExcept((byte)0);
        }
        Assert.That(hasInk, Is.EqualTo(!assignEmptyGeometry));
    }

    [Test]
    public void PathAndOffsetAnimationRoundTripWithTheText()
    {
        TextOnPathEffect path = AlongLine(123);
        path.FollowRotation.Value = false;
        path.PathOffset.SetKeyFrameAnimation(TestAnimations.Linear(0f, -20f, MediaTime.FromSeconds(1), useGlobalClock: true));
        TextBlock block = CreateText("AB", path);

        var restored = (TextBlock)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(block), typeof(TextBlock));
        Assert.That(restored.FilterEffect.Value, Is.TypeOf<TextOnPathEffect>());
        using var resource = restored.ToResource(AtTime(0.5));
        var restoredPath = (TextOnPathEffect.Resource)resource.FilterEffect!;
        Assert.That(restoredPath.Path!.Bounds.Width, Is.EqualTo(123));
        Assert.That(restoredPath.FollowRotation, Is.False);
        Assert.That(restoredPath.PathOffset, Is.EqualTo(-10));
    }

    private static TextOnPathEffect AlongLine(float length)
    {
        var figure = new PathFigure { StartPoint = { Value = new Point(40, 80) } };
        figure.Segments.Add(new LineSegment(40 + length, 80));
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new TextOnPathEffect { Path = { Value = geometry } };
    }

    private static void AssertBounds(Rect actual, Rect expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.002));
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.002));
        Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(0.002));
        Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(0.002));
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

    private static DrawableGroup CreatePathGroup(TextOnPathEffect path, FilterEffect effect, bool pathFirst = true)
    {
        var group = new DrawableGroup { FilterEffect = { Value = effect } };
        TextBlock onPath = CreateText("AB", path);
        onPath.Transform.Value = new TranslateTransform(7, 9);
        TextBlock sibling = CreateText("CD");
        group.Children.Add(pathFirst ? onPath : sibling);
        group.Children.Add(pathFirst ? sibling : onPath);
        return group;
    }

    private static TextBlock CreateText(string content, FilterEffect? effect = null) => new()
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

    private static void Render(Drawable drawable, double seconds = 0)
    {
        using var resource = drawable.ToResource(AtTime(seconds));
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
