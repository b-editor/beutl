using System.Numerics;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Primitives;

namespace Beutl.UnitTests.Engine.Graphics3D;

// feature 003 (FR-033 / FR-027): 3D picking takes LOGICAL coordinates, but the 3D surface is rendered at
// ceil(logical × RenderScale) DEVICE pixels. Renderer3D.ToDevice multiplies the logical pick point by
// RenderScale before HitTester3D builds the NDC ray, so a fixed logical point must resolve to the SAME object
// regardless of the preview/export output scale. Without the × RenderScale conversion, picking on a
// reduced-scale preview is off by that factor.
//
// This exercises HitTester3D.HitTest with EXACTLY the inputs Renderer3D.HitTest feeds it after ToDevice
// (logical × scale, ceil(size × scale)) — the whole ray-cast pick path is pure CPU, so no GPU is needed. (The
// test lives in Beutl.UnitTests rather than the convention's tests/Beutl.Graphics3DTests because the latter is
// still an Exe render harness, not an NUnit project; Beutl.UnitTests already references Beutl.Engine and has
// InternalsVisibleTo, so the 3D types are reachable here.)
[TestFixture]
public class HitTestRenderScaleTests
{
    // Even logical dimensions so ceil(size × scale) is exact at 0.5/1/2 — the device aspect ratio then matches
    // the logical aspect ratio at every scale, making the NDC ray (and thus the pick) deterministic.
    private const int LogicalWidth = 800;
    private const int LogicalHeight = 600;

    // Mirrors Scene3DRenderNode.Process: device px = ceil(logical × scale).
    private static int ToDeviceExtent(int logical, float scale) =>
        scale == 1f ? logical : (int)MathF.Ceiling(logical * scale);

    // Mirrors Renderer3D.ToDevice: logical pick point -> device pick point.
    private static Point ToDevicePoint(Point logical, float scale) =>
        scale == 1f ? logical : logical * scale;

    // A minimal pickable scene: one unit sphere at the origin viewed head-on down -Z, so the logical
    // screen-centre point lands on the sphere. CPU-only — HitTester3D + the CPU mesh path need no GPU.
    private static (Camera3D.Resource Camera, Sphere3D.Resource Sphere) BuildScene(CompositionContext context)
    {
        var camera = new PerspectiveCamera();
        camera.Position.CurrentValue = new Vector3(0, 0, 5);
        camera.Target.CurrentValue = Vector3.Zero;
        camera.Up.CurrentValue = Vector3.UnitY;
        camera.FieldOfView.CurrentValue = 45f;
        camera.NearPlane.CurrentValue = 0.1f;
        camera.FarPlane.CurrentValue = 100f;
        var cameraResource = (Camera3D.Resource)camera.ToResource(context);

        var sphere = new Sphere3D();
        sphere.Position.CurrentValue = Vector3.Zero;
        sphere.Radius.CurrentValue = 1.0f;
        var sphereResource = (Sphere3D.Resource)sphere.ToResource(context);

        return (cameraResource, sphereResource);
    }

    // The screen-centre logical point projects to NDC (0,0) — straight down the camera axis — onto the origin
    // sphere. At scale s, ToDevice gives (W/2·s, H/2·s) and width = ceil(W·s), so ndcX = 2·(W/2·s)/(W·s) − 1 = 0
    // for every s; drop the ·s in ToDevice and the NDC collapses, so the centre point would miss at s != 1.
    [TestCase(0.5f)]
    [TestCase(1.0f)]
    [TestCase(2.0f)]
    public void CenterLogicalPoint_HitsSameObject_RegardlessOfScale(float outputScale)
    {
        var context = new CompositionContext(TimeSpan.Zero);
        (Camera3D.Resource camera, Sphere3D.Resource sphere) = BuildScene(context);
        var objects = new List<Object3D.Resource> { sphere };

        var logicalPoint = new Point(LogicalWidth / 2f, LogicalHeight / 2f);
        int deviceWidth = ToDeviceExtent(LogicalWidth, outputScale);
        int deviceHeight = ToDeviceExtent(LogicalHeight, outputScale);
        Point devicePoint = ToDevicePoint(logicalPoint, outputScale);

        Object3D.Resource? hit = HitTester3D.HitTest(devicePoint, deviceWidth, deviceHeight, camera, objects);

        Assert.That(hit, Is.SameAs(sphere),
            $"the logical centre point should hit the origin sphere at output scale {outputScale}");

        sphere.Dispose();
        camera.Dispose();
    }

    // Negative guard: an off-axis logical point that clears the sphere must MISS at every scale too, proving the
    // conversion is consistent rather than that everything always hits.
    [TestCase(0.5f)]
    [TestCase(1.0f)]
    [TestCase(2.0f)]
    public void CornerLogicalPoint_MissesObject_RegardlessOfScale(float outputScale)
    {
        var context = new CompositionContext(TimeSpan.Zero);
        (Camera3D.Resource camera, Sphere3D.Resource sphere) = BuildScene(context);
        var objects = new List<Object3D.Resource> { sphere };

        var logicalPoint = new Point(5, 5); // top-left corner, well outside the centred sphere
        int deviceWidth = ToDeviceExtent(LogicalWidth, outputScale);
        int deviceHeight = ToDeviceExtent(LogicalHeight, outputScale);
        Point devicePoint = ToDevicePoint(logicalPoint, outputScale);

        Object3D.Resource? hit = HitTester3D.HitTest(devicePoint, deviceWidth, deviceHeight, camera, objects);

        Assert.That(hit, Is.Null, $"the corner logical point should miss at output scale {outputScale}");

        sphere.Dispose();
        camera.Dispose();
    }

    // The load-bearing invariant in isolation: Renderer3D.ToDevice (× scale) composed with the NDC formula must
    // map the logical centre to NDC (0,0) at every scale. Deleting the × scale would collapse it to (−1, +1).
    [TestCase(0.5f)]
    [TestCase(1.0f)]
    [TestCase(2.0f)]
    public void LogicalCenter_MapsToConstantNdc_AcrossScales(float scale)
    {
        var logical = new Point(LogicalWidth / 2f, LogicalHeight / 2f);
        int dw = ToDeviceExtent(LogicalWidth, scale);
        int dh = ToDeviceExtent(LogicalHeight, scale);
        Point device = ToDevicePoint(logical, scale);

        double ndcX = (2.0 * device.X / dw) - 1.0;
        double ndcY = 1.0 - (2.0 * device.Y / dh);

        Assert.That(ndcX, Is.EqualTo(0.0).Within(1e-6));
        Assert.That(ndcY, Is.EqualTo(0.0).Within(1e-6));
    }
}
