using System.Numerics;

using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Backend;

namespace Beutl.UnitTests.Engine.Graphics3D;

// feature 003 (FR-033 / FR-027): 3D picking takes LOGICAL coordinates, but the surface renders at
// ceil(logical × SurfaceDensity) DEVICE pixels. Renderer3D.ToDevice multiplies the logical pick point by
// SurfaceDensity before HitTester3D builds the NDC ray, so a fixed logical point resolves to the SAME object at
// any output scale. Without the × SurfaceDensity conversion, picking on a reduced-scale preview is off by that factor.
//
// This feeds HitTester3D.HitTest EXACTLY what Renderer3D.HitTest feeds it after ToDevice (logical × scale,
// ceil(size × scale)); the ray-cast pick path is pure CPU, so no GPU is needed. It lives in Beutl.UnitTests
// rather than tests/Beutl.Graphics3DTests because the latter is an Exe render harness, not an NUnit project,
// and Beutl.UnitTests already references Beutl.Engine with InternalsVisibleTo.
[TestFixture]
public class HitTestRenderScaleTests
{
    // Even logical dimensions so ceil(size × scale) is exact at 0.5/1/2, keeping the device aspect ratio (and
    // thus the NDC ray and the pick) equal to the logical aspect ratio at every scale.
    private const int LogicalWidth = 800;
    private const int LogicalHeight = 600;

    // Mirrors Scene3DRenderNode.Process: device px = ceil(logical × scale).
    private static int ToDeviceExtent(int logical, float scale) =>
        scale == 1f ? logical : (int)MathF.Ceiling(logical * scale);

    // Mirrors Renderer3D.ToDevice: logical pick point -> device pick point.
    private static Point ToDevicePoint(Point logical, float scale) =>
        scale == 1f ? logical : logical * scale;

    // A minimal pickable scene: one unit sphere at the origin viewed head-on down -Z, so the logical
    // screen-centre point lands on the sphere. CPU-only: HitTester3D and the CPU mesh path need no GPU.
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

    // The screen-centre logical point projects straight down the camera axis to NDC (0,0), onto the origin
    // sphere. At scale s, ToDevice gives (W/2·s, H/2·s) and width = ceil(W·s), so ndcX = 2·(W/2·s)/(W·s) − 1 = 0
    // for every s; drop the ·s and the NDC collapses, so the centre point would miss at s != 1.
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

    // Negative guard: an off-axis logical point that clears the sphere must MISS at every scale, proving the
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

    // End-to-end guard through the REAL Renderer3D: render the sphere at SurfaceDensity = scale (device surface
    // ceil(logical × scale), as Scene3DRenderNode sizes it) and hit-test the LOGICAL centre via the live
    // Renderer3D.HitTest, which applies its own ToDevice(× SurfaceDensity). Unlike the CPU tests above, this
    // catches a regression in Renderer3D.ToDevice itself. Requires a 3D-capable GPU; skips otherwise.
    [TestCase(0.5f)]
    [TestCase(1.0f)]
    [TestCase(2.0f)]
    public void RealRenderer3D_CenterLogicalPoint_HitsSameObject_AcrossScales(float scale)
    {
        IGraphicsContext ctx = VulkanTestEnvironment.EnsureAvailable();
        if (!ctx.Supports3DRendering)
        {
            Assert.Ignore("3D rendering is not supported on this GPU.");
        }

        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var context = new CompositionContext(TimeSpan.Zero);
            (Camera3D.Resource camera, Sphere3D.Resource sphere) = BuildScene(context);
            var objects = new List<Object3D.Resource> { sphere };

            var keyLight = new DirectionalLight3D();
            keyLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, 0, -1));
            keyLight.Color.CurrentValue = Colors.White;
            keyLight.Intensity.CurrentValue = 1f;
            keyLight.IsEnabled = true;
            var lights = new List<Light3D.Resource> { (Light3D.Resource)keyLight.ToResource(context) };

            int deviceWidth = ToDeviceExtent(LogicalWidth, scale);
            int deviceHeight = ToDeviceExtent(LogicalHeight, scale);

            using var renderer = new Renderer3D(ctx);
            renderer.Initialize(deviceWidth, deviceHeight);
            renderer.SurfaceDensity = scale; // mirrors Scene3DRenderNode: device surface is ceil(logical × scale)
            renderer.Render(context, camera, objects, lights, Colors.Black, Colors.White, 0.1f);

            var logicalCenter = new Point(LogicalWidth / 2f, LogicalHeight / 2f);
            Object3D.Resource? hit = renderer.HitTest(logicalCenter);

            Assert.That(hit, Is.SameAs(sphere),
                $"the live Renderer3D.HitTest must hit the centred sphere at render scale {scale}");

            sphere.Dispose();
            camera.Dispose();
        });
    }
}
