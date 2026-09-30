using System.Numerics;

using Beutl.Composition;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Meshes;

namespace Beutl.UnitTests.Engine.Graphics3D;

// 3D space shares the 2D canvas axes (+Y down, +Z away from the viewer) at one unit per pixel.
[TestFixture]
public class CoordinateSystem3DTests
{
    // Projects like HitTester3D unprojects: NDC y up, screen y down.
    private static Vector2 Project(Camera3D.Resource camera, Vector3 world, float width, float height)
    {
        Matrix4x4 viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix(width / height);
        Vector4 clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
        return new Vector2(
            (clip.X / clip.W + 1) / 2 * width,
            (1 - clip.Y / clip.W) / 2 * height);
    }

    [TestCase(0f, 0f)]
    [TestCase(100f, -50f)]
    [TestCase(-640f, 360f)]
    public void DefaultCamera_ShowsTheZeroPlaneLikeTheTwoDimensionalCanvas(float x, float y)
    {
        var camera = (Camera3D.Resource)new PerspectiveCamera().ToResource(CompositionContext.Default);

        Vector2 screen = Project(camera, new Vector3(x, y, 0), 1920, 1080);

        Assert.That(screen.X, Is.EqualTo(960 + x).Within(0.01f));
        Assert.That(screen.Y, Is.EqualTo(540 + y).Within(0.01f));
    }

    [Test]
    public void DefaultCamera_SeesFartherPointsWithPositiveZ()
    {
        var camera = (Camera3D.Resource)new PerspectiveCamera().ToResource(CompositionContext.Default);

        Vector2 near = Project(camera, new Vector3(100, 0, 0), 1920, 1080);
        Vector2 far = Project(camera, new Vector3(100, 0, 500), 1920, 1080);

        Assert.That(far.X - 960, Is.LessThan(near.X - 960));
    }

    [Test]
    public void PlaneMesh_FacesUp()
    {
        PlaneMesh.GeneratePlane(10, 10, 1, 1, out Vertex3D[] vertices, out _);

        Assert.That(vertices.Select(v => v.Normal), Is.All.EqualTo(-Vector3.UnitY));
    }

    [Test]
    public void CubeMesh_PutsItsUpFacingSideAtNegativeY()
    {
        CubeMesh.GenerateCube(2, 4, 6, out Vertex3D[] vertices, out _);

        Assert.That(
            vertices.Where(v => v.Normal == -Vector3.UnitY).Select(v => v.Position.Y),
            Is.All.EqualTo(-2f));
    }
}
