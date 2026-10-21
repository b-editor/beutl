using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using static Beutl.UnitTests.Engine.Graphics.Rendering.RendererTestSupport;

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

        (Rect? boundary, (Rect Bounds, Matrix Transform)? local) = QueryFrame(
            Width,
            Height,
            renderer => (renderer.GetBoundary(ellipse), renderer.GetLocalBoundary(ellipse)),
            ellipse);

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

        (Rect? boundary, (Rect Bounds, Matrix Transform)? local) = QueryFrame(
            Width,
            Height,
            renderer => (renderer.GetBoundary(turned), renderer.GetLocalBoundary(turned)),
            turned);
        (Rect Bounds, Matrix Transform)? uprightLocal =
            QueryFrame(Width, Height, renderer => renderer.GetLocalBoundary(upright), upright);

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

    // A transform effect applied to its target has no bounds of its own, so the drawable's boundary is the frame
    // however it is transformed. A box around that would snap back to the frame after every scale; the drawable's
    // layout box stands in for it, placed by its transform.
    [TestCase(0f)]
    [TestCase(30f)]
    public void LocalBoundary_OfContentThatFillsTheFrame_IsItsLayoutBox(float rotation)
    {
        var rect = new RectShape();
        rect.Width.Value = 100;
        rect.Height.Value = 60;
        var effect = new TransformEffect();
        effect.Transform.Value = new RotationTransform(10);
        ((FilterEffectGroup)rect.FilterEffect.Value!).Children.Add(effect);
        var transform = (TransformGroup)rect.Transform.Value!;
        transform.Children.Add(new TranslateTransform(50, 30));
        transform.Children.Add(new RotationTransform(rotation));

        (Rect Bounds, Matrix Transform)? local =
            QueryFrame(Width, Height, renderer => renderer.GetLocalBoundary(rect), rect);

        Assert.That(local, Is.Not.Null);
        Point center = local!.Value.Transform.Transform(local.Value.Bounds.Center);
        Assert.Multiple(() =>
        {
            AssertRect(local.Value.Bounds, new Rect(0, 0, 100, 60));
            // Turned about its centre, which the translate moves 50 right and 30 down of the frame's centre.
            Assert.That(center.X, Is.EqualTo((Width / 2f) + 50).Within(0.01));
            Assert.That(center.Y, Is.EqualTo((Height / 2f) + 30).Within(0.01));
        });
    }

    // Past the camera plane a perspective transform reflects corners through the origin instead of mapping them
    // onto the drawn quad. The rectangle stays in front of the plane but its blur reaches past it, so only the box
    // of what it draws would be misplaced.
    [Test]
    public void LocalBoundary_IsNull_WhenItsBoxReachesPastTheCameraPlane()
    {
        RectShape rect = CreateBlurredRect(rotation: 0);
        ((Blur)((FilterEffectGroup)rect.FilterEffect.Value!).Children[0]).Sigma.Value = new Size(20, 20);
        // The divisor is 1 - 0.012 * (x - 50) about the rectangle's centre: from 0.4 to 1.6 across its 100 px,
        // and below zero 60 px further right, where the blur still draws.
        rect.Transform.Value = new MatrixTransform(new Matrix(
            1, 0, -0.012f,
            0, 1, 0,
            0, 0, 1));

        (Rect? boundary, (Rect Bounds, Matrix Transform)? local) = QueryFrame(
            Width,
            Height,
            renderer => (renderer.GetBoundary(rect), renderer.GetLocalBoundary(rect)),
            rect);

        Assert.Multiple(() =>
        {
            Assert.That(boundary, Is.Not.Null, "The rectangle is drawn.");
            Assert.That(local, Is.Null);
        });
    }

    [Test]
    public void LocalBoundary_IsNull_WithoutATransformOfItsOwn_OrOutsideTheFrame()
    {
        var group = new DrawableGroup();
        group.Children.Add(CreateShadowedEllipse());
        EllipseShape absent = CreateShadowedEllipse();

        ((Rect Bounds, Matrix Transform)? Group, (Rect Bounds, Matrix Transform)? Absent, Rect? GroupBoundary) result =
            QueryFrame(
                Width,
                Height,
                renderer => (renderer.GetLocalBoundary(group), renderer.GetLocalBoundary(absent), renderer.GetBoundary(group)),
                group);

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
        ellipse.Width.Value = 60;
        ellipse.Height.Value = 60;
        var shadow = new DropShadow();
        shadow.Position.Value = new Point(20, 15);
        shadow.Sigma.Value = new Size(3, 3);
        ((FilterEffectGroup)ellipse.FilterEffect.Value!).Children.Add(shadow);
        return ellipse;
    }

    private static RectShape CreateBlurredRect(float rotation)
    {
        var rect = new RectShape();
        rect.Width.Value = 100;
        rect.Height.Value = 60;
        var blur = new Blur();
        blur.Sigma.Value = new Size(4, 4);
        ((FilterEffectGroup)rect.FilterEffect.Value!).Children.Add(blur);
        rect.Transform.Value = new RotationTransform(rotation);
        return rect;
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
}
