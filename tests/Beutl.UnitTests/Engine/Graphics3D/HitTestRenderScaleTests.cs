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

    [Test]
    public void CapturedDepthAlignsWithTheVisibleForwardPass()
    {
        var graphics = VulkanTestEnvironment.EnsureAvailable();
        if (!graphics.Supports3DRendering) Assert.Ignore("3D rendering is unavailable.");
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            var context = CompositionContext.Default;
            var scene = new Scene3D();
            var sphere = new Sphere3D
            {
                Radius = { Value = 0.55f },
                Position = { Value = new Vector3(0, 0.9f, 0) },
                Material = { Value = new Beutl.Graphics3D.Materials.UnlitMaterial { AlphaMode = { Value = Beutl.Graphics3D.Materials.AlphaMode.Opaque } } }
            };
            scene.Camera.Value = new PerspectiveCamera { Position = { Value = new Vector3(0, 0, 5) } };
            scene.Objects.Add(sphere);
            using var resource = scene.ToResource(context);
            Assert.That(resource.TryCopyDepth([], out var size), Is.False);
            Assert.That(size, Is.EqualTo(default(PixelSize)));
            var renderer = resource.Renderer = new Renderer3D(graphics);
            renderer.Initialize(64, 48);
            Assert.That(resource.TryCopyDepth([], out size), Is.False);
            Assert.That(size, Is.EqualTo(default(PixelSize)));
            renderer.Render(context, resource.Camera!, resource.Objects, [], Colors.Black, Colors.White, 1);
            Assert.That(resource.TryCopyDepth([], out size), Is.False);
            Assert.That(size, Is.EqualTo(new PixelSize(64, 48)));
            var depth = new float[size.Width * size.Height];
            Assert.That(resource.TryCopyDepth(depth, out _), Is.True);
            float[] tooSmall = [-1];
            Assert.That(resource.TryCopyDepth(tooSmall, out size), Is.False);
            Assert.That(tooSmall, Is.EqualTo(new[] { -1f }));
            var repeated = new float[depth.Length];
            Assert.That(resource.TryCopyDepth(repeated, out _), Is.True);
            Assert.That(repeated, Is.EqualTo(depth));
            using (var surface = renderer.CreateSkiaSurface())
            using (var image = surface!.Snapshot())
            using (var pixels = SkiaSharp.SKBitmap.FromImage(image))
            {
                var visible = Enumerable.Range(0, depth.Length).Where(i => pixels.GetPixel(i % size.Width, i / size.Width).Red > 200).ToArray();
                Assert.That(visible.Length, Is.GreaterThan(8));
                Assert.That(visible.All(i => depth[i] < 1), Is.True, "Even the forward material's depth must line up with the color output.");
                int interior = visible[visible.Length / 2];
                Assert.That(depth[(size.Height - 1 - interior / size.Width) * size.Width + interior % size.Width], Is.EqualTo(1));
            }

            renderer.Resize(64, 48);
            Assert.That(resource.TryCopyDepth(repeated, out _), Is.True);
            Assert.That(repeated, Is.EqualTo(depth));
            renderer.Resize(80, 60);
            Assert.That(resource.TryCopyDepth([], out size), Is.False);
            Assert.That(size, Is.EqualTo(default(PixelSize)));
            renderer.Render(context, resource.Camera!, resource.Objects, [], Colors.Black, Colors.White, 1);
            Assert.That(resource.TryCopyDepth([], out size), Is.False);
            Assert.That(size, Is.EqualTo(new PixelSize(80, 60)));
            Assert.That(resource.TryCopyDepth(new float[size.Width * size.Height], out _), Is.True);
            renderer.Dispose();
            Assert.That(resource.TryCopyDepth([], out size), Is.False);
            Assert.That(size, Is.EqualTo(default(PixelSize)));
        });
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
