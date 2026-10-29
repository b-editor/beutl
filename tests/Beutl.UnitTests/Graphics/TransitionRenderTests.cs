using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Graphics;

[TestFixture]
[NonParallelizable]
public partial class TransitionRenderTests
{
    private const int Width = 64;
    private const int Height = 32;

    private static readonly Color s_red = Color.FromArgb(255, 255, 0, 0);
    private static readonly Color s_blue = Color.FromArgb(255, 0, 0, 255);

    // Two opaque clips summed with weights that add up to one stay opaque: nothing beneath the layer may
    // show through the middle of a cross dissolve.
    [Test]
    public void CrossDissolve_KeepsTwoOpaqueClipsOpaque()
    {
        Bgra8888 pixel = RenderCenter(new CrossDissolveTransition(), progress: 0.5);

        Assert.Multiple(() =>
        {
            Assert.That(pixel.A, Is.EqualTo(255));
            Assert.That(pixel.R, Is.InRange(100, 230));
            Assert.That(pixel.B, Is.InRange(100, 230));
            Assert.That(pixel.G, Is.LessThan(8));
        });
    }

    [TestCase(0.1, true)]
    [TestCase(0.9, false)]
    public void CrossDissolve_LeansTowardTheNearerClip(double progress, bool redWins)
    {
        Bgra8888 pixel = RenderCenter(new CrossDissolveTransition(), progress);

        Assert.That(pixel.R > pixel.B, Is.EqualTo(redWins));
    }

    [Test]
    public void Fade_PassesThroughTransparentAtTheMidpoint()
    {
        var fade = new FadeTransition();

        Bgra8888 middle = RenderCenter(fade, progress: 0.5);
        Bgra8888 early = RenderCenter(fade, progress: 0.25);
        Bgra8888 late = RenderCenter(fade, progress: 0.75);

        Assert.Multiple(() =>
        {
            Assert.That(middle.A, Is.LessThan(4));
            Assert.That(early.A, Is.InRange(100, 160));
            Assert.That(early.R, Is.GreaterThan(240), "the first half fades the outgoing clip");
            Assert.That(late.A, Is.InRange(100, 160));
            Assert.That(late.B, Is.GreaterThan(240), "the second half fades the incoming clip");
        });
    }

    [Test]
    public void AnEnterWithNothingBefore_FadesTheElementIn()
    {
        using Bitmap bitmap = Render(new CrossDissolveTransition(), progress: 0.5, withOutgoing: false);
        Bgra8888 pixel = bitmap.GetRow<Bgra8888>(Height / 2)[Width / 2];

        Assert.Multiple(() =>
        {
            Assert.That(pixel.A, Is.InRange(100, 160));
            Assert.That(pixel.B, Is.GreaterThan(240), "unpremultiplied, the faded clip keeps its own colour");
            Assert.That(pixel.R, Is.LessThan(8));
        });
    }

    // Halfway through, the incoming clip fills the half of the frame it enters from and the outgoing clip
    // the half it leaves through, with nothing showing through the seam.
    [Test]
    public void ADirectionalTransition_BringsTheIncomingClipInFromBehind(
        [Values(typeof(WipeTransition), typeof(PushTransition), typeof(SlideTransition))] Type type,
        [Values] ClipTransitionDirection direction)
    {
        ClipTransition transition = CreateDirectional(type, direction);
        using Bitmap bitmap = Render(transition, progress: 0.5);
        (int X, int Y) behind = direction switch
        {
            ClipTransitionDirection.LeftToRight => (4, Height / 2),
            ClipTransitionDirection.RightToLeft => (Width - 5, Height / 2),
            ClipTransitionDirection.TopToBottom => (Width / 2, 3),
            _ => (Width / 2, Height - 4),
        };
        (int X, int Y) ahead = (Width - 1 - behind.X, Height - 1 - behind.Y);
        Bgra8888 behindPixel = bitmap.GetRow<Bgra8888>(behind.Y)[behind.X];
        Bgra8888 aheadPixel = bitmap.GetRow<Bgra8888>(ahead.Y)[ahead.X];

        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(behindPixel), Is.True, $"behind the edge {behindPixel}");
            Assert.That(IsRed(aheadPixel), Is.True, $"ahead of the edge {aheadPixel}");
            Assert.That(bitmap.GetRow<Bgra8888>(Height / 2)[Width / 2].A, Is.EqualTo(255), "the edge must not let the background through");
        });
    }

    [TestCase(false, 0, 255, 0)]
    [TestCase(true, 255, 255, 255)]
    public void Dip_ReachesItsColourAtTheMidpoint(bool white, int r, int g, int b)
    {
        ClipTransition dip = white
            ? new DipToWhiteTransition()
            : new DipToColorTransition { Color = { Value = Color.FromArgb(255, 0, 255, 0) } };

        Bgra8888 middle = RenderCenter(dip, progress: 0.5);
        Bgra8888 early = RenderCenter(dip, progress: 0.2);
        Bgra8888 late = RenderCenter(dip, progress: 0.8);

        Assert.Multiple(() =>
        {
            Assert.That((middle.R, middle.G, middle.B, middle.A), Is.EqualTo((r, g, b, 255)));
            Assert.That(early.A, Is.EqualTo(255));
            Assert.That(early.R, Is.GreaterThan(early.B), "the first half fades the outgoing clip");
            Assert.That(late.A, Is.EqualTo(255));
            Assert.That(late.B, Is.GreaterThan(late.R), "the second half fades the incoming clip in");
        });
    }

    // An adjustment element draws the picture beneath it through its effects. Fading it in must blend the
    // adjusted picture with the plain one, not drop the adjustment or the picture beneath.
    [Test]
    public void AnAdjustmentElement_FadesItsEffectIn()
    {
        VulkanTestEnvironment.EnsureAvailable();
        Bgra8888 Render(double progress) => VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene(Width, Height, string.Empty);
            var background = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(3) }, ZIndex = { Value = 0 } };
            background.Objects.Add(CreateRect(s_red));
            scene.Children.Add(background);

            var backdrop = new SourceBackdrop { Clear = { Value = true } };
            backdrop.FilterEffect.Value = new Invert();
            var adjustment = new Element { Start = { Value = MediaTime.FromSeconds(1) }, Length = { Value = MediaTime.FromSeconds(2) }, ZIndex = { Value = 1 } };
            adjustment.Objects.Add(backdrop);
            adjustment.EnterTransition.Value = new CrossDissolveTransition { Duration = { Value = MediaTime.FromSeconds(1) } };
            scene.Children.Add(adjustment);

            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(progress)));
            using Bitmap snapshot = renderer.Snapshot();
            using Bitmap srgb = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
            return srgb.GetRow<Bgra8888>(Height / 2)[Width / 2];
        });

        Bgra8888 middle = Render(0.5);
        Bgra8888 done = Render(1);

        Assert.Multiple(() =>
        {
            Assert.That((done.R, done.G, done.B), Is.EqualTo((0, 255, 255)), "past the transition the picture is fully inverted");
            Assert.That(middle.A, Is.EqualTo(255));
            Assert.That(middle.R, Is.InRange(100, 230), "half the plain picture");
            Assert.That(middle.G, Is.InRange(100, 230), "half the inverted picture");
        });
    }

    [Test]
    public void HitTest_InsideATransitionSelectsTheIncomingClip()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Scene scene, RectShape incoming) = CreateScene(new CrossDissolveTransition(), withOutgoing: true);
            using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
            var frame = renderer.Compositor.EvaluateGraphics(TimeAt(0.5));
            renderer.Render(frame);

            Drawable? hit = renderer.HitTest(frame, new Point(Width / 2f, Height / 2f));

            Assert.That(hit, Is.SameAs(incoming));
        });
    }

    // The outgoing clip covers only the left half of the frame, so where it shows says whether it moved.
    [Test]
    public void Push_MovesTheOutgoingClipOutAheadOfTheIncomingOne()
    {
        using Bitmap bitmap = Render(new PushTransition(), progress: 0.5, outgoing: CreateLeftHalf(s_red));

        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(bitmap, 4, Height / 2)), Is.True, "the incoming clip fills the left half");
            Assert.That(IsRed(Pixel(bitmap, (Width * 3) / 4, Height / 2)), Is.True, "the outgoing clip has moved into the right half");
        });
    }

    [Test]
    public void Slide_MovesOnlyTheIncomingClip()
    {
        using Bitmap bitmap = Render(new SlideTransition(), progress: 0.5, outgoing: CreateLeftHalf(s_red));

        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(bitmap, 4, Height / 2)), Is.True, "the incoming clip covers the left half");
            Assert.That(Pixel(bitmap, (Width * 3) / 4, Height / 2).A, Is.LessThan(4), "the outgoing clip stays in the left half");
        });
    }

    // A small square in the middle of the outgoing clip grows as the clip zooms in.
    [Test]
    public void Zoom_MagnifiesTheOutgoingClipAboutTheCentre()
    {
        static RectShape Square()
        {
            RectShape square = CreateRect(s_red);
            square.Width.Value = 16;
            square.Height.Value = 16;
            return square;
        }

        using Bitmap zoomed = Render(new ZoomTransition(), progress: 0.5, outgoing: Square());
        using Bitmap dissolved = Render(new CrossDissolveTransition(), progress: 0.5, outgoing: Square());

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(zoomed, (Width / 2) - 10, Height / 2).R, Is.GreaterThan(100), "the square reaches past its own edge");
            Assert.That(Pixel(dissolved, (Width / 2) - 10, Height / 2).R, Is.LessThan(8), "without the zoom it does not");
            Assert.That(Pixel(zoomed, Width / 2, Height / 2).A, Is.EqualTo(255));
        });
    }

    [Test]
    public void Iris_OpensOrClosesAroundTheCentre([Values] IrisTransitionDirection direction, [Values] bool withOutgoing)
    {
        using Bitmap bitmap = Render(new IrisTransition { Direction = { Value = direction } }, progress: 0.5, withOutgoing: withOutgoing);
        bool opening = direction == IrisTransitionDirection.In;
        Bgra8888 inside = Pixel(bitmap, Width / 2, Height / 2);
        Bgra8888 outside = Pixel(bitmap, 1, 1);

        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(opening ? inside : outside), Is.True, "the revealed side");
            Assert.That(IsBlue(Pixel(bitmap, (Width / 2) + 10, Height / 2)), Is.EqualTo(opening), "inside the circle");
            Assert.That(IsBlue(Pixel(bitmap, (Width / 2) + 25, Height / 2)), Is.EqualTo(!opening), "outside the circle");
            Bgra8888 covered = opening ? outside : inside;
            Assert.That(withOutgoing ? IsRed(covered) : covered.A == 0, Is.True, "the outgoing side or empty background");
        });
    }

    [Test]
    public void Iris_PreservesBothEndpoints([Values] IrisTransitionDirection direction)
    {
        var transition = new IrisTransition { Direction = { Value = direction } };
        using Bitmap first = Render(transition, progress: 0);
        using Bitmap last = Render(transition, progress: 1);
        Assert.That(ReadPixels(first).All(IsRed), Is.True);
        Assert.That(ReadPixels(last).All(IsBlue), Is.True, "including corners");
    }

    [Test]
    public void ZoomDirection_ExpandsOrShrinksAnInsetWithoutFillingItsTransparentSurroundings(
        [Values(typeof(ZoomTransition), typeof(ZoomBlurTransition))] Type type,
        [Values(1f, 0.5f)] float scale)
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var transition = (ClipTransition)Activator.CreateInstance(type)!;
            (Scene scene, RectShape incoming) = CreateScene(transition, withOutgoing: false);
            incoming.Width.Value = incoming.Height.Value = 16;
            incoming.Fill.Value = new SolidColorBrush(Color.FromArgb(128, 0, 0, 255));
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery, renderScale: scale);
            foreach (ZoomTransitionDirection direction in new[] { ZoomTransitionDirection.In, ZoomTransitionDirection.Out, ZoomTransitionDirection.In })
            {
                if (transition is ZoomTransition zoom) zoom.Direction.Value = direction;
                else ((ZoomBlurTransition)transition).Direction.Value = direction;

                foreach (double progress in new[] { 0, 0.5, 1 })
                {
                    renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(progress)));
                    using Bitmap snapshot = renderer.Snapshot();
                    using Bitmap bitmap = snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
                    Assert.That(Pixel(bitmap, 0, 0).A, Is.Zero, "empty surroundings must not gain reflected copies");
                    if (progress == 0)
                        Assert.That(bitmap.GetPixelSpan<Bgra8888>().ToArray().All(p => p.A == 0), Is.True);
                    else if (progress == 1)
                        Assert.That(bitmap.GetPixelSpan<Bgra8888>().ToArray().Count(p => p.A > 0), Is.EqualTo(256 * scale * scale));
                    else
                    {
                        Assert.That(Pixel(bitmap, (int)(32 * scale), (int)(16 * scale)).A, Is.InRange(62, 66), "the clip's alpha is preserved");
                        if (direction == ZoomTransitionDirection.In)
                            Assert.That(Pixel(bitmap, (int)(22 * scale), (int)(16 * scale)).A, Is.GreaterThan(16), "expands past the original left edge at 24");
                        else
                            Assert.That(Pixel(bitmap, (int)(25 * scale), (int)(16 * scale)).A, Is.LessThan(8), "shrinks inside that edge");
                    }
                }
            }
        });
    }

    [TestCase(ClipTransitionOrientation.Horizontal)]
    [TestCase(ClipTransitionOrientation.Vertical)]
    public void Split_OpensABandThroughTheCentre(ClipTransitionOrientation orientation)
    {
        using Bitmap bitmap = Render(new SplitTransition { Orientation = { Value = orientation } }, progress: 0.5);
        bool horizontal = orientation == ClipTransitionOrientation.Horizontal;

        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(bitmap, Width / 2, Height / 2)), Is.True, "inside the band");
            Assert.That(IsRed(horizontal ? Pixel(bitmap, 4, Height / 2) : Pixel(bitmap, Width / 2, 2)), Is.True, "before the band");
            Assert.That(IsRed(horizontal ? Pixel(bitmap, Width - 5, Height / 2) : Pixel(bitmap, Width / 2, Height - 3)), Is.True, "after the band");
        });
    }

    // A transition defined outside the engine, as an extension would, draws itself instead of falling back
    // to a cross dissolve.
    [Test]
    public void ATransitionDefinedOutsideTheEngine_DrawsItself()
    {
        Bgra8888 early = RenderCenter(new CutAtTheMiddleTransition(), progress: 0.25);
        Bgra8888 late = RenderCenter(new CutAtTheMiddleTransition(), progress: 0.75);

        Assert.Multiple(() =>
        {
            Assert.That(IsRed(early), Is.True, $"before the middle {early}");
            Assert.That(IsBlue(late), Is.True, $"after the middle {late}");
        });
    }

    // The incoming clip is a left half drawn first and a small square in the top right drawn last; a click
    // selects whichever is under it, and the outgoing clip where neither is.
    [Test]
    public void HitTest_InsideATransitionSelectsTheDrawableUnderThePoint()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Scene scene, RectShape outgoingShape, RectShape? leftHalf, RectShape corner) =
                CreateCornerScene(new CrossDissolveTransition(), withLeftHalf: true);

            using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
            var frame = renderer.Compositor.EvaluateGraphics(TimeAt(0.5));
            renderer.Render(frame);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.HitTest(frame, new Point(4, Height / 2f)), Is.SameAs(leftHalf));
                Assert.That(renderer.HitTest(frame, new Point(Width - 3, 3)), Is.SameAs(corner));
                Assert.That(renderer.HitTest(frame, new Point((Width * 3) / 4f, Height - 3)), Is.SameAs(outgoingShape));
            });
        });
    }

    // Halfway through a push the incoming clip is drawn half a frame to the left, so its top-right square
    // shows just left of the centre and its left half is out of the frame.
    [Test]
    public void HitTest_InsideAMovingTransitionFollowsWhereTheDrawableIsShown()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Scene scene, RectShape outgoingShape, _, RectShape corner) =
                CreateCornerScene(new PushTransition(), withLeftHalf: true);

            using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
            var frame = renderer.Compositor.EvaluateGraphics(TimeAt(0.5));
            renderer.Render(frame);

            Rect? cornerBounds = renderer.GetBoundary(corner);

            Assert.Multiple(() =>
            {
                Assert.That(renderer.HitTest(frame, new Point((Width / 2f) - 3, 3)), Is.SameAs(corner));
                Assert.That(renderer.HitTest(frame, new Point((Width * 3) / 4f, Height / 2f)), Is.SameAs(outgoingShape));
                Assert.That(cornerBounds?.Left, Is.EqualTo((Width / 2f) - 8).Within(0.5f), "the selection follows the push");
                Assert.That(cornerBounds?.Width, Is.EqualTo(8).Within(0.5f));
            });
        });
    }

    // The preview's transform handles sit on the box a drawable draws in its own space, so inside a moving
    // transition that box has to be placed through the transition too.
    [Test]
    public void LocalBoundary_InsideAMovingTransitionIsPlacedWhereTheDrawableIsShown()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Scene scene, _, _, RectShape corner) = CreateCornerScene(new PushTransition(), withLeftHalf: false);

            using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(0.5)));

            (Rect Bounds, Matrix Transform)? local = renderer.GetLocalBoundary(corner);

            Assert.That(local, Is.Not.Null);
            (Rect bounds, Matrix transform) = local!.Value;
            Point topLeft = transform.Transform(bounds.TopLeft);
            Point bottomRight = transform.Transform(bounds.BottomRight);
            Assert.Multiple(() =>
            {
                Assert.That(bounds.Width, Is.EqualTo(8).Within(0.5f));
                Assert.That(bounds.Height, Is.EqualTo(8).Within(0.5f));
                Assert.That(topLeft.X, Is.EqualTo((Width / 2f) - 8).Within(0.5f), "the box follows the push");
                Assert.That(topLeft.Y, Is.EqualTo(0).Within(0.5f));
                Assert.That(bottomRight.X, Is.EqualTo(Width / 2f).Within(0.5f));
                Assert.That(bottomRight.Y, Is.EqualTo(8).Within(0.5f));
            });
        });
    }

    // A split clips the incoming clip to a band and the outgoing clip to the rest. A drawable sized by the frame
    // gets its layout box rather than either part, so it keeps the same box on whichever side of the split it is.
    [Test]
    public void LocalBoundary_InsideASplitIsNotCutToTheSplitsBand()
    {
        VulkanTestEnvironment.EnsureAvailable();
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var scene = new Scene(Width, Height, string.Empty);
            var outgoing = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(1) } };
            outgoing.Objects.Add(CreateRect(s_red));
            scene.Children.Add(outgoing);

            RectShape fillsTheFrame = CreateRect(s_blue);
            fillsTheFrame.Width.Value = 16;
            fillsTheFrame.Height.Value = 8;
            var effect = new TransformEffect();
            effect.Transform.Value = new RotationTransform(10);
            ((FilterEffectGroup)fillsTheFrame.FilterEffect.Value!).Children.Add(effect);
            var incoming = new Element { Start = { Value = MediaTime.FromSeconds(1) }, Length = { Value = MediaTime.FromSeconds(2) } };
            incoming.Objects.Add(fillsTheFrame);
            incoming.EnterTransition.Value = new SplitTransition { Duration = { Value = MediaTime.FromSeconds(1) } };
            scene.Children.Add(incoming);

            using var renderer = new SceneRenderer(scene, RenderIntent.Preview);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(0.5)));

            (Rect Bounds, Matrix Transform)? local = renderer.GetLocalBoundary(fillsTheFrame);

            Assert.That(local, Is.Not.Null);
            Rect box = local!.Value.Bounds.TransformToAABB(local.Value.Transform);
            // The 16 x 8 rectangle sits at the frame's centre; the band is the middle 32 px of the frame.
            Assert.Multiple(() =>
            {
                Assert.That(box.X, Is.EqualTo((Width - 16) / 2f).Within(0.5f), "the box is not cut to the band");
                Assert.That(box.Y, Is.EqualTo((Height - 8) / 2f).Within(0.5f));
                Assert.That(box.Width, Is.EqualTo(16).Within(0.5f));
                Assert.That(box.Height, Is.EqualTo(8).Within(0.5f));
            });
        });
    }

    private static ClipTransition CreateDirectional(Type type, ClipTransitionDirection direction)
    {
        if (type == typeof(PushTransition)) return new PushTransition { Direction = { Value = direction } };
        if (type == typeof(SlideTransition)) return new SlideTransition { Direction = { Value = direction } };
        return new WipeTransition { Direction = { Value = direction } };
    }

    private static Bgra8888 Pixel(Bitmap bitmap, int x, int y) => bitmap.GetRow<Bgra8888>(y)[x];

    private static RectShape CreateLeftHalf(Color color)
    {
        RectShape shape = CreateRect(color);
        shape.Width.Value = Width / 2;
        shape.AlignmentX.Value = AlignmentX.Left;
        return shape;
    }

    private static bool IsRed(Bgra8888 pixel) => pixel is { A: 255, R: > 240, B: < 16 };

    private static bool IsBlue(Bgra8888 pixel) => pixel is { A: 255, B: > 240, R: < 16 };

    private static Bgra8888 RenderCenter(ClipTransition transition, double progress)
    {
        using Bitmap bitmap = Render(transition, progress);
        return bitmap.GetRow<Bgra8888>(Height / 2)[Width / 2];
    }

    // The outgoing clip is red and the incoming clip blue, both covering the whole frame over a transparent
    // background; the result is read back unpremultiplied in sRGB.
    private static Bitmap Render(ClipTransition transition, double progress, bool withOutgoing = true, RectShape? outgoing = null)
    {
        VulkanTestEnvironment.EnsureAvailable();
        return VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            (Scene scene, _) = CreateScene(transition, withOutgoing, outgoing);
            using var renderer = new SceneRenderer(scene, RenderIntent.Delivery);
            renderer.Render(renderer.Compositor.EvaluateGraphics(TimeAt(progress)));
            using Bitmap snapshot = renderer.Snapshot();
            return snapshot.Convert(BitmapColorType.Bgra8888, BitmapAlphaType.Unpremul, BitmapColorSpace.Srgb);
        });
    }

    // The transition is the incoming clip's one-second enter transition, which starts at one second.
    private static MediaTime TimeAt(double progress)
        => MediaTime.FromSeconds(1 + progress) - (progress >= 1 ? MediaTime.FromTimeSpan(TimeSpan.FromTicks(1)) : MediaTime.Zero);

    private static (Scene Scene, RectShape Incoming) CreateScene(
        ClipTransition transition, bool withOutgoing, RectShape? outgoing = null)
    {
        var scene = new Scene(Width, Height, string.Empty);
        if (withOutgoing)
        {
            var outgoingElement = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(1) } };
            outgoingElement.Objects.Add(outgoing ?? CreateRect(s_red));
            scene.Children.Add(outgoingElement);
        }

        RectShape incoming = CreateRect(s_blue);
        var incomingElement = new Element { Start = { Value = MediaTime.FromSeconds(1) }, Length = { Value = MediaTime.FromSeconds(2) } };
        incomingElement.Objects.Add(incoming);
        transition.Duration.Value = MediaTime.FromSeconds(1);
        incomingElement.EnterTransition.Value = transition;
        scene.Children.Add(incomingElement);
        return (scene, incoming);
    }

    // A red outgoing clip and an incoming clip with an 8 px square at its top right and, if asked, a left half.
    private static (Scene Scene, RectShape Outgoing, RectShape? LeftHalf, RectShape Corner) CreateCornerScene(
        ClipTransition transition, bool withLeftHalf)
    {
        var scene = new Scene(Width, Height, string.Empty);
        RectShape outgoingShape = CreateRect(s_red);
        var outgoing = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(1) } };
        outgoing.Objects.Add(outgoingShape);
        scene.Children.Add(outgoing);

        RectShape? leftHalf = withLeftHalf ? CreateLeftHalf(s_blue) : null;
        RectShape corner = CreateRect(s_blue);
        corner.Width.Value = 8;
        corner.Height.Value = 8;
        corner.AlignmentX.Value = AlignmentX.Right;
        corner.AlignmentY.Value = AlignmentY.Top;
        var incoming = new Element { Start = { Value = MediaTime.FromSeconds(1) }, Length = { Value = MediaTime.FromSeconds(2) } };
        if (leftHalf is not null)
            incoming.Objects.Add(leftHalf);
        incoming.Objects.Add(corner);
        transition.Duration.Value = MediaTime.FromSeconds(1);
        incoming.EnterTransition.Value = transition;
        scene.Children.Add(incoming);
        return (scene, outgoingShape, leftHalf, corner);
    }

    private static RectShape CreateRect(Color color)
    {
        var shape = new RectShape();
        shape.Width.Value = Width;
        shape.Height.Value = Height;
        shape.Fill.Value = new SolidColorBrush(color);
        return shape;
    }
}

// Draws the outgoing clip up to the middle of the transition and the incoming clip after it, through the
// same public hook an extension's transition overrides.
public sealed partial class CutAtTheMiddleTransition : ClipTransition
{
    public CutAtTheMiddleTransition()
    {
        ScanProperties<CutAtTheMiddleTransition>();
    }

    public new partial class Resource
    {
        public override void Draw(ClipTransitionContext context)
        {
            context.DrawClip(context.Progress < 0.5f ? context.From : context.To);
        }
    }
}
