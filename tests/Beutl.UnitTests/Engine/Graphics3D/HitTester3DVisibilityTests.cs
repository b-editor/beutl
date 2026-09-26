using System.Numerics;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics3D;

// Picking answers only for what the camera actually shows.
[TestFixture]
public class HitTester3DVisibilityTests
{
    private static readonly Point s_center = new(960, 540);

    private static Camera3D.Resource CreateCamera(float farPlane)
    {
        var camera = new PerspectiveCamera();
        camera.FarPlane.CurrentValue = farPlane;
        return (Camera3D.Resource)camera.ToResource(CompositionContext.Default);
    }

    [TestCase(0f, true)]
    [TestCase(2000f, false)]
    public void Objects_BeyondTheFarPlane_AreNotHit(float z, bool expected)
    {
        using var camera = CreateCamera(farPlane: 1500);
        var cube = new Cube3D();
        cube.Position.CurrentValue = new Vector3(0, 0, z);
        using var resource = (Object3D.Resource)cube.ToResource(CompositionContext.Default);

        Object3D.Resource? hit = HitTester3D.HitTest(s_center, 1920, 1080, camera, [resource]);

        Assert.That(hit is not null, Is.EqualTo(expected));
    }

    // A plane is one-sided: turned toward the camera it is drawn and hit; turned away it is culled and missed.
    // Mirroring reverses its winding, so rendering then shows the other side (checked against a GPU render).
    [TestCase(90f, 1f, true)]
    [TestCase(-90f, 1f, false)]
    [TestCase(90f, -1f, false)]
    [TestCase(-90f, -1f, true)]
    public void OneSidedPlane_IsHitOnlyOnTheSideRenderingShows(float rotationX, float scaleX, bool expected)
    {
        using var camera = CreateCamera(farPlane: 10000);
        var plane = new Plane3D();
        plane.Rotation.CurrentValue = new Vector3(rotationX, 0, 0);
        plane.Scale.CurrentValue = new Vector3(scaleX, 1, 1);
        using var resource = (Object3D.Resource)plane.ToResource(CompositionContext.Default);

        Object3D.Resource? hit = HitTester3D.HitTest(s_center, 1920, 1080, camera, [resource]);

        Assert.That(hit is not null, Is.EqualTo(expected));
    }

    // Two squares 300 px apart make one card; the gap between them shows nothing and lets clicks through.
    [TestCase(-300f, true)]
    [TestCase(0f, false)]
    [TestCase(300f, true)]
    public void Card_AnswersOnlyWhereItsDrawablesShow(float x, bool expected)
    {
        using var camera = CreateCamera(farPlane: 10000);
        var card = new DrawableObject3D();
        card.Children.Add(CreateSquare(-300));
        card.Children.Add(CreateSquare(300));
        using var resource = (DrawableObject3D.Resource)card.ToResource(CompositionContext.Default);
        resource.UpdateLayout(new Size(1920, 1080), density: 1);

        Object3D.Resource? hit = HitTester3D.HitTest(new Point(960 + x, 540), 1920, 1080, camera, [resource]);

        Assert.That(hit is not null, Is.EqualTo(expected));
    }

    [Test]
    public void Card_WithAFullyTransparentMaterial_IsNotHit()
    {
        using var camera = CreateCamera(farPlane: 10000);
        var card = new DrawableObject3D();
        card.Children.Add(CreateSquare(0));
        ((Beutl.Graphics3D.Materials.UnlitMaterial)card.Material.CurrentValue!).Opacity.CurrentValue = 0;
        using var resource = (DrawableObject3D.Resource)card.ToResource(CompositionContext.Default);
        resource.UpdateLayout(new Size(1920, 1080), density: 1);

        Assert.That(HitTester3D.HitTest(s_center, 1920, 1080, camera, [resource]), Is.Null);
    }

    private static RectShape CreateSquare(float x)
    {
        var rect = new RectShape();
        rect.Width.CurrentValue = 100;
        rect.Height.CurrentValue = 100;
        rect.Fill.CurrentValue = Brushes.Red;
        rect.Transform.CurrentValue = new TranslateTransform(x, 0);
        return rect;
    }
}
