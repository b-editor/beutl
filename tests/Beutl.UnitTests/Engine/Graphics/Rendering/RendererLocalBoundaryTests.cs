using System.Collections.Immutable;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering;

// The preview's transform handles are drawn on these bounds, so they have to be what the drawable draws (its
// effects included) in its own space, and map onto the boundary the preview outlines in the frame.
[NonParallelizable]
[TestFixture]
public class RendererLocalBoundaryTests
{
    private const int Width = 320;
    private const int Height = 240;

    [Test]
    public void LocalBoundary_IncludesWhatAnEffectDraws_AndMapsOntoTheBoundary()
    {
        EllipseShape ellipse = CreateShadowedEllipse();

        (Rect? boundary, (Rect Bounds, Matrix Transform)? local) =
            Measure(renderer => (renderer.GetBoundary(ellipse), renderer.GetLocalBoundary(ellipse)), ellipse);

        Assert.That(local, Is.Not.Null);
        Rect mapped = MapToAABB(local!.Value.Bounds, local.Value.Transform);
        Assert.Multiple(() =>
        {
            // The shadow is drawn 20 right and 15 below the 60x60 ellipse, so the box reaches past the
            // ellipse there instead of being the ellipse's layout box.
            Assert.That(local.Value.Bounds.Right, Is.GreaterThan(60 + 20));
            Assert.That(local.Value.Bounds.Bottom, Is.GreaterThan(60 + 15));
            AssertRect(mapped, boundary!.Value);
        });
    }

    [Test]
    public void LocalBoundary_OfATurnedDrawable_TurnsWithIt()
    {
        RectShape turned = CreateBlurredRect(rotation: 30);
        RectShape upright = CreateBlurredRect(rotation: 0);

        (Rect? boundary, (Rect Bounds, Matrix Transform)? local) =
            Measure(renderer => (renderer.GetBoundary(turned), renderer.GetLocalBoundary(turned)), turned);
        (Rect Bounds, Matrix Transform)? uprightLocal = Measure(renderer => renderer.GetLocalBoundary(upright), upright);

        Assert.That(local, Is.Not.Null);
        Assert.That(uprightLocal, Is.Not.Null);
        (Rect bounds, Matrix transform) = local!.Value;
        Point topLeft = transform.Transform(bounds.TopLeft);
        Point topRight = transform.Transform(bounds.TopRight);
        Assert.Multiple(() =>
        {
            // Measured inside the transform: the turn does not grow the box, it turns it.
            AssertRect(bounds, uprightLocal!.Value.Bounds);
            Assert.That(
                MathF.Atan2(topRight.Y - topLeft.Y, topRight.X - topLeft.X) * 180 / MathF.PI,
                Is.EqualTo(30).Within(0.01));
            // The blur is even on every side and the rectangle turns about its centre, which stays centred.
            Point center = transform.Transform(bounds.Center);
            Assert.That(center.X, Is.EqualTo(Width / 2f).Within(0.01));
            Assert.That(center.Y, Is.EqualTo(Height / 2f).Within(0.01));
            AssertRect(MapToAABB(bounds, transform), boundary!.Value);
        });
    }

    [Test]
    public void LocalBoundary_IsNull_WithoutATransformOfItsOwn_OrOutsideTheFrame()
    {
        var group = new DrawableGroup();
        group.Children.Add(CreateShadowedEllipse());
        EllipseShape absent = CreateShadowedEllipse();

        ((Rect Bounds, Matrix Transform)? Group, (Rect Bounds, Matrix Transform)? Absent, Rect? GroupBoundary) result =
            Measure(renderer => (renderer.GetLocalBoundary(group), renderer.GetLocalBoundary(absent), renderer.GetBoundary(group)), group);

        Assert.Multiple(() =>
        {
            Assert.That(result.GroupBoundary, Is.Not.Null, "The group is in the frame and draws its child.");
            Assert.That(result.Group, Is.Null, "A group lays its children out under a node of its own.");
            Assert.That(result.Absent, Is.Null);
        });
    }

    private static EllipseShape CreateShadowedEllipse()
    {
        var ellipse = new EllipseShape();
        ellipse.Width.CurrentValue = 60;
        ellipse.Height.CurrentValue = 60;
        var shadow = new DropShadow();
        shadow.Position.CurrentValue = new Point(20, 15);
        shadow.Sigma.CurrentValue = new Size(3, 3);
        ((FilterEffectGroup)ellipse.FilterEffect.CurrentValue!).Children.Add(shadow);
        return ellipse;
    }

    private static RectShape CreateBlurredRect(float rotation)
    {
        var rect = new RectShape();
        rect.Width.CurrentValue = 100;
        rect.Height.CurrentValue = 60;
        var blur = new Blur();
        blur.Sigma.CurrentValue = new Size(4, 4);
        ((FilterEffectGroup)rect.FilterEffect.CurrentValue!).Children.Add(blur);
        rect.Transform.CurrentValue = new RotationTransform(rotation);
        return rect;
    }

    private static T Measure<T>(Func<Renderer, T> query, params Drawable[] drawables)
    {
        return RenderThread.Dispatcher.Invoke(() =>
        {
            using var renderer = new Renderer(
                Width,
                Height,
                RenderIntent.Preview,
                renderScale: 1,
                maxWorkingScale: float.PositiveInfinity,
                surface: new CpuRenderTarget(Width, Height));
            ImmutableArray<EngineObject.Resource> resources =
                [.. drawables.Select(d => (EngineObject.Resource)d.ToResource(CompositionContext.Default))];
            renderer.UpdateFrame(new CompositionFrame(
                resources,
                new TimeRange(TimeSpan.Zero, TimeSpan.FromSeconds(1)),
                new PixelSize(Width, Height),
                null));
            return query(renderer);
        });
    }

    private static Rect MapToAABB(Rect bounds, Matrix transform)
    {
        Point[] corners =
        [
            transform.Transform(bounds.TopLeft),
            transform.Transform(bounds.TopRight),
            transform.Transform(bounds.BottomRight),
            transform.Transform(bounds.BottomLeft),
        ];
        return new Rect(
            new Point(corners.Min(p => p.X), corners.Min(p => p.Y)),
            new Point(corners.Max(p => p.X), corners.Max(p => p.Y)));
    }

    private static void AssertRect(Rect actual, Rect expected)
    {
        Assert.That(actual.X, Is.EqualTo(expected.X).Within(0.01), "X");
        Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(0.01), "Y");
        Assert.That(actual.Width, Is.EqualTo(expected.Width).Within(0.01), "Width");
        Assert.That(actual.Height, Is.EqualTo(expected.Height).Within(0.01), "Height");
    }

    private sealed class CpuRenderTarget(int width, int height)
        : RenderTarget(
            SKSurface.Create(new SKImageInfo(
                width,
                height,
                SKColorType.RgbaF16,
                SKAlphaType.Premul,
                SKColorSpace.CreateSrgbLinear())),
            width,
            height);
}
