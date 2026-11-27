using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Gizmo;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Skyboxes;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class SkyboxAuthoringContractTests
{
    [Test]
    public void ARendererThatImplementsTheOriginalRenderMethodStillWorks()
    {
        using var legacy = new LegacyRenderer();
        IRenderer3D renderer = legacy;
        CompositionContext context = CompositionContext.Default;
        using var camera = (Camera3D.Resource)new PerspectiveCamera().ToResource(context);
        using var skybox = (Skybox3D.Resource)new ProceduralSkybox().ToResource(context);

        renderer.Render(context, camera, [], [], Colors.Transparent, Colors.White, 1f);
        renderer.Render(context, camera, [], [], Colors.Transparent, Colors.White, 1f,
            gizmoTarget: null, gizmoMode: GizmoMode.None, skybox);

        Assert.That(legacy.RenderCount, Is.EqualTo(2));
    }

    private sealed class LegacyRenderer : IRenderer3D
    {
        public int Width => 0;
        public int Height => 0;
        public float SurfaceDensity => 1f;
        public int RenderCount { get; private set; }

        public void Render(CompositionContext compositionContext, Camera3D.Resource camera,
            IReadOnlyList<Object3D.Resource> objects, IReadOnlyList<Light3D.Resource> lights,
            Color backgroundColor, Color ambientColor, float ambientIntensity,
            Object3D.Resource? gizmoTarget, GizmoMode gizmoMode)
            => RenderCount++;

        public void Initialize(int width, int height) => throw new NotSupportedException();
        public void Resize(int width, int height) => throw new NotSupportedException();
        public SKSurface? CreateSkiaSurface() => throw new NotSupportedException();
        public byte[] DownloadPixels() => throw new NotSupportedException();
        public Object3D.Resource? HitTest(Point screenPoint) => throw new NotSupportedException();
        public IReadOnlyList<Object3D.Resource> HitTestWithPath(Point screenPoint) => throw new NotSupportedException();
        public GizmoAxis GizmoHitTest(Point screenPoint, Object3D.Resource? gizmoTarget, GizmoMode gizmoMode)
            => throw new NotSupportedException();

        public void Dispose() { }
    }
}
