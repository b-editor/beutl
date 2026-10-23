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

    // One unit sphere at origin viewed head-on: screen centre lands on it. CPU-only.
    private static (Camera3D.Resource Camera, Sphere3D.Resource Sphere) BuildScene(CompositionContext context)
    {
        var camera = new PerspectiveCamera();
        camera.Position.Value = new Vector3(0, 0, 5);
        camera.Target.Value = Vector3.Zero;
        camera.Up.Value = Vector3.UnitY;
        camera.FieldOfView.Value = 45f;
        camera.NearPlane.Value = 0.1f;
        camera.FarPlane.Value = 100f;
        var cameraResource = (Camera3D.Resource)camera.ToResource(context);

        var sphere = new Sphere3D();
        sphere.Position.Value = Vector3.Zero;
        sphere.Radius.Value = 1.0f;
        var sphereResource = (Sphere3D.Resource)sphere.ToResource(context);

        return (cameraResource, sphereResource);
    }

    // End-to-end through the real Renderer3D.HitTestWithPath. Requires a 3D-capable GPU.
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
            var context = new CompositionContext(MediaTime.Zero);
            (Camera3D.Resource camera, Sphere3D.Resource sphere) = BuildScene(context);
            var objects = new List<Object3D.Resource> { sphere };

            var keyLight = new DirectionalLight3D();
            keyLight.Direction.Value = Vector3.Normalize(new Vector3(0, 0, -1));
            keyLight.Color.Value = Colors.White;
            keyLight.Intensity.Value = 1f;
            keyLight.IsEnabled.Value = true;
            var lights = new List<Light3D.Resource> { (Light3D.Resource)keyLight.ToResource(context) };

            int deviceWidth = (int)MathF.Ceiling(LogicalWidth * scale);
            int deviceHeight = (int)MathF.Ceiling(LogicalHeight * scale);

            using var renderer = new Renderer3D(ctx);
            renderer.Initialize(deviceWidth, deviceHeight);
            renderer.SurfaceDensity = scale; // mirrors Scene3DRenderNode: device surface is ceil(logical × scale)
            renderer.Render(context, camera, objects, lights, Colors.Black, Colors.White, 0.1f);

            var logicalCenter = new Point(LogicalWidth / 2f, LogicalHeight / 2f);
            Object3D.Resource hit = renderer.HitTestWithPath(logicalCenter).Last();

            Assert.That(hit, Is.SameAs(sphere),
                $"the live Renderer3D.HitTestWithPath must hit the centred sphere at render scale {scale}");

            sphere.Dispose();
            camera.Dispose();
        });
    }
}
