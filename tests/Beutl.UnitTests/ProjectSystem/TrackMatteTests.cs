using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Composition;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Engine;
using Beutl.UnitTests.Engine.Graphics.Backend;
using Beutl.UnitTests.Engine.Graphics.Rendering;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class TrackMatteTests
{
    private const int Width = 64;
    private const int Height = 32;

    [TestCase(MatteMode.Alpha, 128)]
    [TestCase(MatteMode.InvertedAlpha, 127)]
    [TestCase(MatteMode.Luma, 27)]
    [TestCase(MatteMode.InvertedLuma, 228)]
    public void Modes_UseSourceAlphaAndPremultipliedLuminance(MatteMode mode, int expected)
    {
        (Scene scene, _, _) = CreateScene(mode, Color.FromArgb(128, 255, 0, 0));
        using var render = new SceneRender(scene);
        using Bitmap bitmap = render.Draw();
        Assert.That(Pixel(bitmap, 8, 16).A, Is.EqualTo(expected).Within(2));
        Assert.That(Pixel(bitmap, 48, 16).A,
            Is.EqualTo(mode is MatteMode.InvertedAlpha or MatteMode.InvertedLuma ? 255 : 0));
    }

    [TestCase(MatteMode.Alpha, 0)]
    [TestCase(MatteMode.InvertedAlpha, 255)]
    [TestCase(MatteMode.Luma, 0)]
    [TestCase(MatteMode.InvertedLuma, 255)]
    public void MissingOrOutOfRangeSource_IsTransparent(MatteMode mode, int alpha)
    {
        (Scene scene, Element source, _) = CreateScene(mode, Colors.White);
        source.Start.Value = Seconds(2);
        using var render = new SceneRender(scene);
        using Bitmap before = render.Draw();
        Assert.That(Pixel(before, 8, 16).A, Is.EqualTo(alpha));
        scene.Children.Remove(source);
        using Bitmap removed = render.Draw(Seconds(2));
        Assert.That(Pixel(removed, 8, 16).A, Is.EqualTo(alpha));
    }

    [Test]
    public void NoneAndNullSource_PreserveOrdinaryOutput_AndHiddenSourcesStillSupplyMattes()
    {
        (Scene scene, Element source, Element target) = CreateScene(MatteMode.None, Colors.White);
        using var render = new SceneRender(scene);
        using Bitmap none = render.Draw();
        Assert.That(Pixel(none, 48, 16).A, Is.EqualTo(255));
        target.MatteMode.Value = MatteMode.Alpha;
        target.MatteSourceZIndex.Value = null;
        using Bitmap noSource = render.Draw();
        Assert.That(Pixel(noSource, 48, 16).A, Is.EqualTo(255));
        target.MatteSourceZIndex.Value = 0;
        using Bitmap matte = render.Draw();
        Assert.That(Pixel(matte, 48, 16).A, Is.Zero);
        Assert.That(Pixel(matte, 8, 16).B, Is.EqualTo(255));
        source.IsEnabled.Value = false;
        using Bitmap disabled = render.Draw();
        Assert.That(Pixel(disabled, 8, 16).A, Is.Zero);
    }

    [Test]
    public void MatteIsAppliedToTheWholeElement_WithoutMaskingTheBackground()
    {
        (Scene scene, _, Element target) = CreateScene(MatteMode.Alpha, Colors.White);
        target.Objects.Add(Rect(Colors.Red, Width / 4));
        Add(scene, -1, Rect(Colors.Green));
        using var render = new SceneRender(scene);
        using Bitmap bitmap = render.Draw();
        Assert.Multiple(() =>
        {
            Assert.That(Pixel(bitmap, 8, 16).R, Is.EqualTo(255));
            Assert.That(Pixel(bitmap, 24, 16).B, Is.EqualTo(255));
            Assert.That(Pixel(bitmap, 48, 16).G, Is.EqualTo(128).Within(1));
            Assert.That(Pixel(bitmap, 48, 16).A, Is.EqualTo(255));
        });
    }

    [TestCase(MatteSourceKind.Above, 4, 1, 3)]
    [TestCase(MatteSourceKind.Below, 8, 11, 9)]
    public void RelativeSources_SkipEmptyLayers_AndSwitchAtClipBoundaries(
        MatteSourceKind kind, int nearLayer, int farLayer, int movedLayer)
    {
        var scene = new Scene(Width, Height, string.Empty);
        Element near = Add(scene, nearLayer, Rect(Colors.White, Width / 2));
        near.IsVisibleInOutput.Value = false;
        near.Start.Value = Seconds(1);
        near.Length.Value = Seconds(1);
        Element far = Add(scene, farLayer, Rect(Colors.White));
        far.IsVisibleInOutput.Value = false;
        Element target = Add(scene, 6, Rect(Colors.Blue));
        target.MatteSourceKind.Value = kind;
        target.MatteMode.Value = MatteMode.Alpha;
        using var preview = new SceneRender(scene);
        using var delivery = new SceneRender(scene, RenderIntent.Delivery);
        foreach (double time in new[] { 0, 1, 1.5, 2, 0.5 })
        {
            using Bitmap bitmap = preview.Draw(Seconds(time));
            using Bitmap exported = delivery.Draw(Seconds(time));
            Assert.That(Pixel(bitmap, 8, 16).A, Is.EqualTo(255));
            Assert.That(Pixel(bitmap, 48, 16).A, Is.EqualTo(time is >= 1 and < 2 ? 0 : 255));
            for (int y = 0; y < Height; y++)
                Assert.That(bitmap.GetRow<Bgra8888>(y).ToArray(), Is.EqualTo(exported.GetRow<Bgra8888>(y).ToArray()));
        }

        target.ZIndex.Value = movedLayer;
        using Bitmap moved = preview.Draw(Seconds(1));
        Assert.That(Pixel(moved, 48, 16).A, Is.EqualTo(255));
        far.IsEnabled.Value = false;
        using Bitmap disabled = preview.Draw(Seconds(1));
        Assert.That(Pixel(disabled, 8, 16).A, Is.Zero);
        scene.Children.Remove(far);
        using Bitmap missing = preview.Draw(Seconds(1));
        Assert.That(Pixel(missing, 8, 16).A, Is.Zero, "no clip in the selected direction supplies no alpha");
        target.MatteMode.Value = MatteMode.InvertedAlpha;
        using Bitmap inverted = preview.Draw(Seconds(1));
        Assert.That(Pixel(inverted, 8, 16).A, Is.EqualTo(255));
    }

    [Test]
    public void RelativeSource_UsesFrameTimeWhileTheTargetIsHeldByATransition()
    {
        var scene = new Scene(Width, Height, string.Empty);
        Element far = Add(scene, 1, Rect(Colors.White));
        far.IsVisibleInOutput.Value = false;
        Element near = Add(scene, 4, Rect(Colors.White, Width / 2));
        near.Start.Value = Seconds(2);
        near.IsVisibleInOutput.Value = false;
        Element target = Add(scene, 6, Rect(Colors.Blue));
        target.Length.Value = Seconds(2);
        target.MatteMode.Value = MatteMode.Alpha;
        var incoming = new Element { ZIndex = { Value = 6 }, Start = { Value = Seconds(2) }, Length = { Value = Seconds(2) } };
        incoming.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(1) } };
        scene.Children.Add(incoming);
        using var render = new SceneRender(scene);
        using Bitmap bitmap = render.Draw(Seconds(2.25));
        Assert.That(Pixel(bitmap, 8, 16).A, Is.GreaterThan(0));
        Assert.That(Pixel(bitmap, 48, 16).A, Is.Zero);
    }

    [Test]
    public void AnimatedMatte_UpdatesCachedFramesAndMatchesDelivery()
    {
        (Scene scene, Element source, _) = CreateScene(MatteMode.Alpha, Colors.White);
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 16 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = Seconds(1) }, Value = { Value = 64 }, Easing = { Value = new LinearEasing() } });
        ((RectShape)source.Objects[0]).Width.SetKeyFrameAnimation(animation);
        using var preview = new SceneRender(scene);
        using var delivery = new SceneRender(scene, RenderIntent.Delivery);
        for (int i = 0; i < 6; i++) preview.Draw().Dispose();
        Assert.That(HasCachedNode(preview.Root), Is.True, "exercise invalidation after persistent node caching");
        using Bitmap early = preview.Draw();
        using Bitmap late = preview.Draw(Seconds(1));
        using Bitmap exported = delivery.Draw(Seconds(1));
        Assert.That(Pixel(early, 48, 16).A, Is.Zero);
        Assert.That(Pixel(late, 48, 16).A, Is.EqualTo(255));
        for (int y = 0; y < Height; y++)
            Assert.That(late.GetRow<Bgra8888>(y).ToArray(), Is.EqualTo(exported.GetRow<Bgra8888>(y).ToArray()));
        source.Objects.Clear();
        using Bitmap removed = preview.Draw(Seconds(1));
        Assert.That(Pixel(removed, 8, 16).A, Is.Zero);
    }

    [TestCase(BlendMode.Multiply, 255, 0, 255)]
    [TestCase(BlendMode.Multiply, 128, 187, 255)]
    [TestCase(BlendMode.DstOut, 255, 0, 0)]
    [TestCase(BlendMode.DstOut, 128, 255, 127)]
    public void Matte_PreservesBlendingAgainstTheScene(BlendMode blend, int maskAlpha, int red, int alpha)
    {
        (Scene scene, _, Element target) = CreateScene(MatteMode.Alpha, Color.FromArgb((byte)maskAlpha, 255, 255, 255));
        ((Drawable)target.Objects[0]).BlendMode.Value = blend;
        Add(scene, -1, Rect(Colors.Red));
        using var preview = new SceneRender(scene);
        using var delivery = new SceneRender(scene, RenderIntent.Delivery);
        using Bitmap actual = preview.Draw();
        using Bitmap exported = delivery.Draw();
        Assert.Multiple(() =>
        {
            Assert.That(Pixel(actual, 8, 16).R, Is.EqualTo(red).Within(2));
            Assert.That(Pixel(actual, 8, 16).B, Is.Zero);
            Assert.That(Pixel(actual, 8, 16).A, Is.EqualTo(alpha).Within(2));
            Assert.That(Pixel(actual, 48, 16).R, Is.EqualTo(255), "unmasked backdrop survives");
            Assert.That(Pixel(actual, 48, 16).A, Is.EqualTo(255));
            Assert.That(actual.GetRow<Bgra8888>(16).ToArray(), Is.EqualTo(exported.GetRow<Bgra8888>(16).ToArray()));
        });
    }

    [Test]
    public void PortalAndTimeController_ReplayBothMatteInputsAtTheAdjustedTime()
    {
        (Scene scene, Element source, Element target) = CreateScene(MatteMode.InvertedAlpha, Colors.White);
        target.ZIndex.Value = 3;
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.Zero }, Value = { Value = 16 } });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = Seconds(1) }, Value = { Value = 48 } });
        ((RectShape)source.Objects[0]).Width.SetKeyFrameAnimation(animation);
        var portal = new Element { ZIndex = { Value = 2 }, Length = { Value = Seconds(4) } };
        portal.Objects.Add(new PortalObject { Count = { Value = 1 } });
        var controller = new DrawableTimeController();
        portal.Objects.Add(controller);
        scene.Children.Add(portal);
        using var render = new SceneRender(scene);
        using Bitmap initial = render.Draw();
        Assert.That(Pixel(initial, 24, 16).A, Is.EqualTo(255), "default controller preserves the imported matte");
        controller.OffsetPosition.Value = Seconds(1);
        using Bitmap offset = render.Draw();
        Assert.That(Pixel(offset, 24, 16).A, Is.Zero, "matte animation follows the replay time");
        Assert.That(Pixel(offset, 56, 16).B, Is.EqualTo(255), "content and inverted mode survive replay");
        controller.OffsetPosition.Value = MediaTime.Zero;
        using Bitmap restored = render.Draw();
        Assert.That(Pixel(restored, 24, 16).A, Is.EqualTo(255));
    }

    [TestCase(MatteSourceKind.Above, 2, 0)]
    [TestCase(MatteSourceKind.Below, 8, 10)]
    public void ReplayedRelativeMatte_ReselectsTheSourceAtTheAdjustedTime(MatteSourceKind kind, int nearLayer, int farLayer)
    {
        var scene = new Scene(Width, Height, string.Empty);
        Element far = Add(scene, farLayer, Rect(Colors.White));
        far.IsVisibleInOutput.Value = false;
        Element near = Add(scene, nearLayer, Rect(Colors.White, Width / 2));
        near.Start.Value = Seconds(1);
        near.Length.Value = Seconds(1);
        near.IsVisibleInOutput.Value = false;
        Element target = Add(scene, 5, Rect(Colors.Blue));
        target.MatteSourceKind.Value = kind;
        target.MatteMode.Value = MatteMode.Alpha;
        var portal = new Element { ZIndex = { Value = -1 }, Length = { Value = Seconds(4) } };
        portal.Objects.Add(new PortalObject { Count = { Value = 6 } });
        var controller = new DrawableTimeController();
        portal.Objects.Add(controller);
        scene.Children.Add(portal);
        using var preview = new SceneRender(scene);
        using var delivery = new SceneRender(scene, RenderIntent.Delivery);
        foreach (double offset in new[] { 0, 1, 1.5, 2, 4, 0 })
        {
            controller.OffsetPosition.Value = Seconds(offset);
            using Bitmap actual = preview.Draw();
            using Bitmap exported = delivery.Draw();
            Assert.That(Pixel(actual, 8, 16).A, Is.EqualTo(offset == 4 ? 0 : 255), $"offset {offset}");
            Assert.That(Pixel(actual, 48, 16).A, Is.EqualTo(offset is >= 1 and < 2 or 4 ? 0 : 255), $"offset {offset}");
            Assert.That(actual.GetRow<Bgra8888>(16).ToArray(), Is.EqualTo(exported.GetRow<Bgra8888>(16).ToArray()));
        }
        near.Objects.Clear();
        near.Objects.Add(new Beutl.Graphics3D.Scene3D());
        controller.OffsetPosition.Value = Seconds(1);
        using (var compositor = new SceneCompositor(scene))
            Assert.That(compositor.EvaluateGraphics(MediaTime.Zero).ContainsScene3D, Is.True);
        scene.Children.Remove(far);
        scene.Children.Remove(near);
        using Bitmap absent = preview.Draw();
        Assert.That(Pixel(absent, 8, 16).A, Is.Zero, "an absent source or a cycle through the importing portal supplies no matte");
    }

    [Test]
    public void Matte_BlendsMultipleDrawablesAsOneMaskedChange_AndRefreshesTheBackdrop()
    {
        (Scene scene, _, Element target) = CreateScene(MatteMode.Alpha, Color.FromArgb(128, 255, 255, 255));
        RectShape eraser = Rect(Colors.White, 16);
        eraser.BlendMode.Value = BlendMode.DstOut;
        target.Objects.Add(eraser);
        RectShape background = Rect(Colors.Red);
        Add(scene, -1, background);
        using var render = new SceneRender(scene);
        for (int frame = 0; frame < 6; frame++) render.Draw().Dispose();
        using Bitmap first = render.Draw();
        Assert.That(Pixel(first, 8, 16).A, Is.EqualTo(127).Within(2));
        // The working surface is linear; half intensity becomes about 188 in the sRGB snapshot.
        Assert.That(Pixel(first, 24, 16).R, Is.EqualTo(187).Within(2));
        Assert.That(Pixel(first, 24, 16).B, Is.EqualTo(188).Within(2));
        background.Fill.Value = new SolidColorBrush(Colors.Lime);
        using Bitmap changed = render.Draw();
        Assert.That(Pixel(changed, 24, 16).G, Is.EqualTo(187).Within(2));
        Assert.That(Pixel(changed, 48, 16).G, Is.EqualTo(255));
    }

    [Test]
    public void SharedSource_IsRecordedOncePerFrame_AndRefreshesAfterAnEdit()
    {
        (Scene scene, Element source, _) = CreateScene(MatteMode.Alpha, Colors.White);
        Element second = Add(scene, 2, Rect(Colors.Red, Width / 4));
        second.MatteSourceKind.Value = MatteSourceKind.Layer;
        second.MatteSourceZIndex.Value = 0;
        second.MatteMode.Value = MatteMode.Alpha;
        using var render = new SceneRender(scene);
        render.Update(MediaTime.Zero);
        using var request = new RenderRequest(new RenderRequestOptions(RenderIntent.Preview,
            RenderRequestPurpose.Frame, targetDomain: new Rect(0, 0, Width, Height)));
        RecordedRenderGraph graph = new RenderRequestRecorder(request).Record(render.Root);
        RenderFragmentId?[] sourceIds = graph.Fragments
            .Where(f => f.Payload is BlendRenderFragmentPayload { BlendMode: BlendMode.DstIn })
            .Select(f => f.Inputs[0].Id).ToArray();
        Assert.That(sourceIds, Has.Length.EqualTo(2));
        Assert.That(sourceIds.Distinct().Count(), Is.EqualTo(1));
        using Bitmap first = render.Draw();
        Assert.That(Pixel(first, 48, 16).A, Is.Zero);
        ((RectShape)source.Objects[0]).Width.Value = Width;
        using Bitmap changed = render.Draw();
        Assert.That(Pixel(changed, 48, 16).A, Is.EqualTo(255));
    }

    [Test]
    public void SourceTransitionAndSolo_AreEvaluatedAtTheFrameTime()
    {
        (Scene scene, Element source, _) = CreateScene(MatteMode.Alpha, Colors.White);
        source.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(1) } };
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 1 }, IsSolo = { Value = true } });
        using var render = new SceneRender(scene);
        using Bitmap half = render.Draw(Seconds(0.5));
        Assert.That(Pixel(half, 8, 16).A, Is.EqualTo(128).Within(2));
        scene.Layers.Add(new TimelineLayer { ZIndex = { Value = 0 }, IsVideoMuted = { Value = true } });
        using Bitmap muted = render.Draw(Seconds(0.5));
        Assert.That(Pixel(muted, 8, 16).A, Is.Zero);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void HiddenTransitionSide_StaysHiddenInOutputButSuppliesTheMatte(bool hideOutgoing)
    {
        (Scene scene, Element outgoing, Element target) = CreateScene(MatteMode.Alpha, Colors.White);
        outgoing.Length.Value = Seconds(2);
        outgoing.IsVisibleInOutput.Value = !hideOutgoing;
        Element incoming = Add(scene, 0, Rect(Colors.White, Width / 2));
        incoming.Start.Value = Seconds(2);
        incoming.Length.Value = Seconds(2);
        incoming.IsVisibleInOutput.Value = hideOutgoing;
        outgoing.ExitTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(0.5) } };
        incoming.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(0.5) } };
        ((RectShape)target.Objects[0]).Width.Value = Width / 4;
        using var render = new SceneRender(scene);
        foreach ((double time, int incomingAlpha) in new[] { (1.25, 0), (1.75, 64), (2.25, 191), (2.75, 255) })
        {
            using Bitmap bitmap = render.Draw(Seconds(time));
            Assert.That(Pixel(bitmap, 8, 16).A, Is.EqualTo(255), $"both sides supply the matte at {time}");
            Assert.That(Pixel(bitmap, 24, 16).A,
                Is.EqualTo(hideOutgoing ? incomingAlpha : 255 - incomingAlpha).Within(2),
                $"only the visible side contributes to output at {time}");
        }

        outgoing.IsVisibleInOutput.Value = incoming.IsVisibleInOutput.Value = false;
        incoming.EnterTransition.Value = new DipToColorTransition { Duration = { Value = Seconds(0.5) } };
        using Bitmap hidden = render.Draw(Seconds(2));
        Assert.That(Pixel(hidden, 24, 16).A, Is.Zero, "a transition must not draw its color when both sides are hidden");
    }

    [Test]
    public void HiddenSource_IsNotDrawnByAnOutputPortal()
    {
        (Scene scene, _, Element target) = CreateScene(MatteMode.Alpha, Colors.White);
        ((RectShape)target.Objects[0]).Width.Value = Width / 4;
        var portal = new Element { ZIndex = { Value = -1 }, Length = { Value = Seconds(4) } };
        portal.Objects.Add(new PortalObject { Count = { Value = 1 } });
        scene.Children.Add(portal);
        using var render = new SceneRender(scene);
        using Bitmap bitmap = render.Draw();
        Assert.That(Pixel(bitmap, 8, 16).A, Is.EqualTo(255), "the hidden source still supplies the matte");
        Assert.That(Pixel(bitmap, 24, 16).A, Is.Zero, "the output portal must respect visibility too");
    }

    [Test]
    public void OutputAndMatteSnapshots_AreIndependentAndReleasedAfterDetachment()
    {
        (Scene scene, Element source, _) = CreateScene(MatteMode.Alpha, Colors.White);
        var application = new BeutlApplication { Project = { Value = new Project() } };
        application.Project.Value!.Items.Add(scene);
        source.IsVisibleInOutput.Value = true;
        using var compositor = new SceneCompositor(scene);
        CompositionFrame frame = compositor.EvaluateGraphics(MediaTime.Zero);
        Drawable.Resource output = frame.Objects.OfType<RectShape.Resource>().Single();
        var presenter = frame.Objects.OfType<TrackMattePresenter.Resource>().Single();
        Drawable.Resource matte = presenter.Matte!.Drawables.Single();
        Assert.That(matte, Is.Not.SameAs(output));

        scene.Children.Remove(source);
        compositor.EvaluateGraphics(MediaTime.Zero);
        Assert.That(output.IsDisposed, Is.True);
        Assert.That(matte.IsDisposed, Is.True);
        Assert.That(presenter.Matte.Drawables, Is.Empty);
        compositor.Dispose();
        Assert.That(presenter.IsDisposed, Is.True);
    }

    [Test]
    public void ChainedAndCyclicMattes_DoNotRecurseIntoTheRenderGraph()
    {
        (Scene scene, Element source, _) = CreateScene(MatteMode.Alpha, Colors.White);
        Element mask = Add(scene, -1, Rect(Colors.White, Width / 4));
        mask.IsVisibleInOutput.Value = false;
        source.MatteSourceKind.Value = MatteSourceKind.Layer;
        source.MatteSourceZIndex.Value = -1;
        source.MatteMode.Value = MatteMode.Alpha;
        using var render = new SceneRender(scene);
        using Bitmap chain = render.Draw();
        Assert.That(Pixel(chain, 8, 16).A, Is.EqualTo(255));
        Assert.That(Pixel(chain, 24, 16).A, Is.Zero);
        mask.MatteSourceKind.Value = MatteSourceKind.Layer;
        mask.MatteSourceZIndex.Value = 0;
        mask.MatteMode.Value = MatteMode.Alpha;
        using Bitmap cycle = render.Draw();
        Assert.That(Pixel(cycle, 8, 16).A, Is.Zero);
    }

    [Test]
    public void TextMatte_CutsTheTargetsColorIntoGlyphs()
    {
        (Scene scene, Element source, _) = CreateScene(MatteMode.Alpha, Colors.White);
        source.Objects.Clear();
        source.Objects.Add(new TextBlock
        {
            Text = { Value = "MATTE" },
            Size = { Value = 15 },
            FontFamily = { Value = TypefaceProvider.Typeface().FontFamily },
        });
        using var render = new SceneRender(scene, RenderIntent.Delivery);
        using Bitmap bitmap = render.Draw();
        int ink = 0;
        int clear = 0;
        for (int y = 0; y < Height; y++)
            foreach (Bgra8888 pixel in bitmap.GetRow<Bgra8888>(y))
            {
                if (pixel.A > 200) { ink++; Assert.That(pixel.B, Is.EqualTo(255).Within(1)); }
                if (pixel.A == 0) clear++;
            }
        Assert.That(ink, Is.GreaterThan(20));
        Assert.That(clear, Is.GreaterThan(Width * Height / 2));
        if (Environment.GetEnvironmentVariable("BEUTL_HEADLESS_CAPTURE_DIR") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            bitmap.Save(Path.Combine(directory, "track-matte-text.png"));
        }
    }

    [TestCase(MatteMode.Alpha)]
    [TestCase(MatteMode.Luma)]
    [TestCase(MatteMode.InvertedAlpha)]
    [TestCase(MatteMode.InvertedLuma)]
    public void NativePreviewAndExport_MatchAndSelectTheMattedContent(MatteMode mode)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Scene scene, Element source, Element target) = CreateScene(mode, Color.FromArgb(128, 255, 0, 0));
            var shape = (RectShape)target.Objects[0];
            using var preview = new SceneRenderer(scene, RenderIntent.Preview);
            using SceneRenderer delivery = ExportRendererFactory.Create(scene, renderScale: 1);
            for (int frame = 0; frame < 2; frame++)
            {
                ((RectShape)source.Objects[0]).Width.Value = frame == 0 ? Width / 2 : Width;
                CompositionFrame composition = preview.Compositor.EvaluateGraphics(Seconds(frame));
                preview.Render(composition);
                delivery.Render(delivery.Compositor.EvaluateGraphics(Seconds(frame)));
                using Bitmap previewSnapshot = preview.Snapshot();
                using Bitmap deliverySnapshot = delivery.Snapshot();
                using Bitmap a = previewSnapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                using Bitmap b = deliverySnapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                for (int y = 0; y < Height; y++)
                    Assert.That(a.GetRow<Bgra8888>(y).ToArray(), Is.EqualTo(b.GetRow<Bgra8888>(y).ToArray()));
                Assert.That(preview.HitTest(composition, new Point(8, 16)), Is.SameAs(shape));
                Assert.That(preview.GetBoundary(shape), Is.Not.Null);
                Assert.That(preview.GetLocalBoundary(shape), Is.Not.Null);
            }

            RectShape background = Rect(Colors.Green);
            Add(scene, -1, background);
            shape.Width.Value = 16;
            CompositionFrame smallTarget = preview.Compositor.EvaluateGraphics(Seconds(1));
            preview.Render(smallTarget);
            Assert.That(preview.HitTest(smallTarget, new Point(24, 16)), Is.SameAs(background),
                "a matte's pixels must not block selection of the layers behind an empty part of its target");

            target.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = Seconds(1) } };
            CompositionFrame transitioning = preview.Compositor.EvaluateGraphics(Seconds(0.5));
            preview.Render(transitioning);
            Assert.That(preview.HitTest(transitioning, new Point(8, 16)), Is.SameAs(shape));
            Assert.That(preview.HitTest(transitioning, new Point(24, 16)), Is.SameAs(background),
                "transition selection must not return a synthetic matte presenter");

            target.EnterTransition.Value = new DipToColorTransition { Duration = { Value = Seconds(1) } };
            CompositionFrame fill = preview.Compositor.EvaluateGraphics(Seconds(0.5));
            preview.Render(fill);
            Assert.That(preview.HitTest(fill, new Point(24, 16)), Is.SameAs(shape),
                "the transition's own fill still selects the document's matted content");
        });
    }

    [Test]
    public void Settings_RoundTripAndUndoTogether_AndFollowLayerReordering()
    {
        using var harness = new SceneHistoryHarness("track-matte");
        Element source = harness.AddElement(0);
        Element target = harness.AddElement(2);
        var service = new ElementAttributeService(harness.History);
        harness.History.Commit("Setup");
        int before = harness.History.UndoCount;
        service.SetTrackMatte(target, MatteSourceKind.Layer, 0, MatteMode.InvertedLuma);
        Assert.That(harness.History.UndoCount, Is.EqualTo(before + 1));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(target.MatteSourceZIndex.Value, Is.Null);
        Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.None));
        Assert.That(harness.History.Redo(), Is.True);
        service.SetVisibleInOutput(target, false);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(target), typeof(Element));
        Assert.That(restored.MatteSourceZIndex.Value, Is.EqualTo(0));
        Assert.That(restored.MatteMode.Value, Is.EqualTo(MatteMode.InvertedLuma));
        Assert.That(restored.IsVisibleInOutput.Value, Is.False);
        new LayerMoveService(harness.History).ApplyMove(harness.Scene, 0, 1, [source]);
        Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(1));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(target.MatteSourceZIndex.Value, Is.EqualTo(0));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(target.IsVisibleInOutput.Value, Is.True);
    }

    [TestCase(MatteSourceKind.Above)]
    [TestCase(MatteSourceKind.Below)]
    public void RelativeSettings_RoundTripAndUndo_AndStayRelativeAfterReordering(MatteSourceKind kind)
    {
        using var harness = new SceneHistoryHarness("relative-track-matte");
        Element target = harness.AddElement(MediaTime.Zero, Seconds(4), 2);
        foreach (int layer in new[] { 0, 3, 5 }) harness.AddElement(MediaTime.Zero, Seconds(4), layer);
        var service = new ElementAttributeService(harness.History);
        harness.History.Commit("Setup");
        Assert.That(target.MatteSourceKind.Value, Is.EqualTo(MatteSourceKind.Above));
        Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.None));
        int before = harness.History.UndoCount;
        service.SetTrackMatte(target, kind, null, MatteMode.InvertedAlpha);
        Assert.That(harness.History.UndoCount, Is.EqualTo(before + 1));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(target.MatteSourceKind.Value, Is.EqualTo(MatteSourceKind.Above));
        Assert.That(target.MatteMode.Value, Is.EqualTo(MatteMode.None));
        Assert.That(harness.History.Redo(), Is.True);
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(target), typeof(Element));
        Assert.That(restored.MatteSourceKind.Value, Is.EqualTo(kind));
        Assert.That(restored.GetMatteSourceZIndex(harness.Scene, MediaTime.Zero), Is.EqualTo(kind == MatteSourceKind.Above ? 0 : 3));

        new LayerMoveService(harness.History).ApplyMove(harness.Scene, 2, 4, [target]);
        Assert.That(target.MatteSourceKind.Value, Is.EqualTo(kind));
        Assert.That(target.GetMatteSourceZIndex(harness.Scene, MediaTime.Zero), Is.EqualTo(kind == MatteSourceKind.Above ? 2 : 5));
        Assert.That(harness.History.Undo(), Is.True);
        Assert.That(target.GetMatteSourceZIndex(harness.Scene, MediaTime.Zero),
            Is.EqualTo(restored.GetMatteSourceZIndex(harness.Scene, MediaTime.Zero)));
    }

    [TestCase(null)]
    [TestCase(0)]
    [TestCase(3)]
    public void OlderSavedMattes_KeepTheirExplicitSource(int? source)
    {
        var element = new Element { ZIndex = { Value = 2 }, MatteMode = { Value = MatteMode.Alpha } };
        element.MatteSourceZIndex.Value = source;
        var json = CoreSerializer.SerializeToJsonObject(element);
        json.Remove(nameof(Element.MatteSourceKind));
        var restored = (Element)CoreSerializer.DeserializeFromJsonObject(json, typeof(Element));
        Assert.That(restored.MatteSourceKind.Value, Is.EqualTo(MatteSourceKind.Layer));
        Assert.That(restored.GetMatteSourceZIndex(new Scene(), MediaTime.Zero), Is.EqualTo(source));
    }

    private static (Scene Scene, Element Source, Element Target) CreateScene(MatteMode mode, Color color)
    {
        var scene = new Scene(Width, Height, string.Empty);
        Element source = Add(scene, 0, Rect(color, Width / 2));
        source.IsVisibleInOutput.Value = false;
        Element target = Add(scene, 1, Rect(Colors.Blue));
        target.MatteSourceKind.Value = MatteSourceKind.Layer;
        target.MatteSourceZIndex.Value = 0;
        target.MatteMode.Value = mode;
        return (scene, source, target);
    }

    private static Element Add(Scene scene, int zIndex, Drawable drawable)
    {
        var element = new Element { ZIndex = { Value = zIndex }, Length = { Value = Seconds(4) } };
        element.Objects.Add(drawable);
        scene.Children.Add(element);
        return element;
    }

    private static RectShape Rect(Color color, int width = Width) => new()
    {
        Width = { Value = width },
        Height = { Value = Height },
        Fill = { Value = new SolidColorBrush(color) },
        AlignmentX = { Value = AlignmentX.Left },
        AlignmentY = { Value = AlignmentY.Top },
    };

    private static bool HasCachedNode(RenderNode node)
    {
        if (node.Cache.IsCached) return true;
        foreach (RenderNode child in node.ChildNodes)
            if (HasCachedNode(child)) return true;
        return false;
    }

    private static Bgra8888 Pixel(Bitmap bitmap, int x, int y) => bitmap.GetRow<Bgra8888>(y)[x];
    private static MediaTime Seconds(double value) => MediaTime.FromSeconds(value);

    private sealed class SceneRender : IDisposable
    {
        private readonly SceneCompositor _compositor;
        private readonly RenderNodeRenderer _renderer;
        private readonly CpuRenderTarget _target = new(Width, Height);
        private readonly ImmediateCanvas _canvas;
        public ContainerRenderNode Root { get; } = new();

        public SceneRender(Scene scene, RenderIntent intent = RenderIntent.Preview)
        {
            _compositor = new SceneCompositor(scene);
            _canvas = new ImmediateCanvas(_target, intent);
            _renderer = new RenderNodeRenderer(Root, new RenderNodeRenderRequest
            {
                Intent = intent,
                TargetDomain = new Rect(0, 0, Width, Height),
                Purpose = RenderRequestPurpose.Frame,
                CacheOptions = intent == RenderIntent.Preview ? RenderCacheOptions.Enabled : RenderCacheOptions.Disabled,
            }, new CpuRenderTargetFactory());
        }

        public void Update(MediaTime time)
        {
            CompositionFrame frame = _compositor.EvaluateGraphics(time);
            using var context = new GraphicsContext2D(Root, new Size(Width, Height));
            context.Clear();
            foreach (Drawable.Resource drawable in frame.Objects.OfType<Drawable.Resource>()) context.DrawDrawable(drawable);
        }

        public Bitmap Draw(MediaTime time = default)
        {
            Update(time);
            _renderer.Render(_canvas);
            using Bitmap snapshot = _target.Snapshot();
            return snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
        }

        public void Dispose()
        {
            _renderer.Dispose();
            Root.Dispose();
            _canvas.Dispose();
            _target.Dispose();
            _compositor.Dispose();
        }
    }
}
