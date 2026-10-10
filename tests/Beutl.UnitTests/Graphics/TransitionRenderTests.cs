using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transitions;
using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Graphics;

[TestFixture]
[NonParallelizable]
public class TransitionRenderTests
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
            : new DipToColorTransition { Color = { CurrentValue = Color.FromArgb(255, 0, 255, 0) } };

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
            var background = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(3), ZIndex = 0 };
            background.Objects.Add(CreateRect(s_red));
            scene.Children.Add(background);

            var backdrop = new SourceBackdrop { Clear = { CurrentValue = true } };
            backdrop.FilterEffect.CurrentValue = new Invert();
            var adjustment = new Element { Start = TimeSpan.FromSeconds(1), Length = TimeSpan.FromSeconds(2), ZIndex = 1 };
            adjustment.Objects.Add(backdrop);
            adjustment.EnterTransition = new CrossDissolveTransition { Duration = { CurrentValue = TimeSpan.FromSeconds(1) } };
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
            square.Width.CurrentValue = 16;
            square.Height.CurrentValue = 16;
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
    public void Iris_OpensTheIncomingClipFromTheCentre()
    {
        using Bitmap bitmap = Render(new IrisTransition(), progress: 0.5);

        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(bitmap, Width / 2, Height / 2)), Is.True, "inside the circle");
            Assert.That(IsBlue(Pixel(bitmap, (Width / 2) + 10, Height / 2)), Is.True, "inside the circle");
            Assert.That(IsRed(Pixel(bitmap, (Width / 2) + 25, Height / 2)), Is.True, "outside the circle");
            Assert.That(IsRed(Pixel(bitmap, 1, 1)), Is.True, "in the corner");
        });
    }

    [Test]
    public void Iris_CoversTheCornersByTheEnd()
    {
        using Bitmap bitmap = Render(new IrisTransition(), progress: 1);

        Assert.That(IsBlue(Pixel(bitmap, 0, 0)), Is.True);
    }

    [TestCase(ClipTransitionOrientation.Horizontal)]
    [TestCase(ClipTransitionOrientation.Vertical)]
    public void Split_OpensABandThroughTheCentre(ClipTransitionOrientation orientation)
    {
        using Bitmap bitmap = Render(new SplitTransition { Orientation = { CurrentValue = orientation } }, progress: 0.5);
        bool horizontal = orientation == ClipTransitionOrientation.Horizontal;

        Assert.Multiple(() =>
        {
            Assert.That(IsBlue(Pixel(bitmap, Width / 2, Height / 2)), Is.True, "inside the band");
            Assert.That(IsRed(horizontal ? Pixel(bitmap, 4, Height / 2) : Pixel(bitmap, Width / 2, 2)), Is.True, "before the band");
            Assert.That(IsRed(horizontal ? Pixel(bitmap, Width - 5, Height / 2) : Pixel(bitmap, Width / 2, Height - 3)), Is.True, "after the band");
        });
    }

    private static ClipTransition CreateDirectional(Type type, ClipTransitionDirection direction)
    {
        if (type == typeof(PushTransition)) return new PushTransition { Direction = { CurrentValue = direction } };
        if (type == typeof(SlideTransition)) return new SlideTransition { Direction = { CurrentValue = direction } };
        return new WipeTransition { Direction = { CurrentValue = direction } };
    }

    private static Bgra8888 Pixel(Bitmap bitmap, int x, int y) => bitmap.GetRow<Bgra8888>(y)[x];

    private static RectShape CreateLeftHalf(Color color)
    {
        RectShape shape = CreateRect(color);
        shape.Width.CurrentValue = Width / 2;
        shape.AlignmentX.CurrentValue = AlignmentX.Left;
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
    private static TimeSpan TimeAt(double progress)
        => TimeSpan.FromSeconds(1 + progress) - (progress >= 1 ? TimeSpan.FromTicks(1) : TimeSpan.Zero);

    private static (Scene Scene, RectShape Incoming) CreateScene(
        ClipTransition transition, bool withOutgoing, RectShape? outgoing = null)
    {
        var scene = new Scene(Width, Height, string.Empty);
        if (withOutgoing)
        {
            var outgoingElement = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(1) };
            outgoingElement.Objects.Add(outgoing ?? CreateRect(s_red));
            scene.Children.Add(outgoingElement);
        }

        RectShape incoming = CreateRect(s_blue);
        var incomingElement = new Element { Start = TimeSpan.FromSeconds(1), Length = TimeSpan.FromSeconds(2) };
        incomingElement.Objects.Add(incoming);
        transition.Duration.CurrentValue = TimeSpan.FromSeconds(1);
        incomingElement.EnterTransition = transition;
        scene.Children.Add(incomingElement);
        return (scene, incoming);
    }

    private static RectShape CreateRect(Color color)
    {
        var shape = new RectShape();
        shape.Width.CurrentValue = Width;
        shape.Height.CurrentValue = Height;
        shape.Fill.CurrentValue = new SolidColorBrush(color);
        return shape;
    }
}
