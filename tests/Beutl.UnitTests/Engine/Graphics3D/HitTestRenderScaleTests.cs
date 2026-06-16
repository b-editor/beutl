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

// 3D picking must work at any output scale: logical pick point * SurfaceDensity -> device coords for NDC ray.
[TestFixture]
public class HitTestRenderScaleTests
{
    // Even dimensions so ceil(size * scale) is exact at 0.5/1/2.
    private const int LogicalWidth = 800;
    private const int LogicalHeight = 600;

    // Mirrors Scene3DRenderNode.Process: device px = ceil(logical × scale).
    private static int ToDeviceExtent(int logical, float scale) =>
        scale == 1f ? logical : (int)MathF.Ceiling(logical * scale);

    // Mirrors Renderer3D.ToDevice: logical pick point -> device pick point.
    private static Point ToDevicePoint(Point logical, float scale) =>
        scale == 1f ? logical : logical * scale;

    // One unit sphere at origin viewed head-on: screen centre lands on it. CPU-only.
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

    // Centre logical point maps to NDC (0,0) at every scale; dropping the *s would break at s != 1.
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

    // Negative guard: an off-axis point must miss at every scale.
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

    // Logical centre must map to NDC (0,0) at every scale.
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

    // End-to-end through real Renderer3D.HitTest. Requires a 3D-capable GPU.
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
