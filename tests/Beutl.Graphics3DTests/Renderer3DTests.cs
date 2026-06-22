using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

/// <summary>
/// Renders the PBR material grid and the four shadow scenes that used to be dumped to PNG by the
/// former console program, and asserts pixel invariants on the downloaded framebuffer instead.
/// The whole fixture skips (<see cref="Assert.Ignore"/>) when no Vulkan/3D-capable GPU is present.
/// </summary>
[TestFixture]
[NonParallelizable]
public class Renderer3DTests
{
    private const int Width = 1200;
    private const int Height = 800;

    private IGraphicsContext _context = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Skips the whole fixture cleanly when Vulkan/MoltenVK/SwiftShader is unavailable.
        _context = GpuTestEnvironment.EnsureAvailable();
    }

    [Test]
    public void RenderPbrMaterialGrid_ProducesLitNonUniformFramebuffer()
    {
        byte[] pixels = GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var renderer = new Renderer3D(_context);
            var objects = new List<Object3D.Resource>();
            var sceneResources = new List<IDisposable>();
            try
            {
                renderer.Initialize(Width, Height);

                var renderContext = new CompositionContext(TimeSpan.Zero);

                var camera = new PerspectiveCamera();
                camera.Position.CurrentValue = new Vector3(0, 0, 12);
                camera.Target.CurrentValue = new Vector3(0, 0, 0);
                camera.Up.CurrentValue = Vector3.UnitY;
                camera.FieldOfView.CurrentValue = 45f;
                camera.NearPlane.CurrentValue = 0.1f;
                camera.FarPlane.CurrentValue = 100f;
                var cameraResource = (PerspectiveCamera.Resource)camera.ToResource(renderContext);
                sceneResources.Add(cameraResource);

                const int gridSizeX = 7; // Metallic steps
                const int gridSizeY = 7; // Roughness steps
                const float sphereRadius = 0.4f;
                const float spacing = 1.1f;
                float offsetX = -(gridSizeX - 1) * spacing * 0.5f;
                float offsetY = -(gridSizeY - 1) * spacing * 0.5f;

                for (int y = 0; y < gridSizeY; y++)
                {
                    float roughness = (float)y / (gridSizeY - 1);
                    for (int x = 0; x < gridSizeX; x++)
                    {
                        float metallic = (float)x / (gridSizeX - 1);

                        var sphere = new Sphere3D();
                        sphere.Position.CurrentValue = new Vector3(offsetX + x * spacing, offsetY + y * spacing, 0);
                        sphere.Radius.CurrentValue = sphereRadius;
                        sphere.Segments.CurrentValue = 32;
                        sphere.Rings.CurrentValue = 16;

                        var material = new PBRMaterial();
                        material.Albedo.CurrentValue = new Color(255, 255, 200, 50);
                        material.Metallic.CurrentValue = metallic;
                        material.Roughness.CurrentValue = roughness;
                        material.AmbientOcclusion.CurrentValue = 1.0f;
                        sphere.Material.CurrentValue = material;

                        objects.Add((Sphere3D.Resource)sphere.ToResource(renderContext));
                    }
                }

                var lights = BuildThreePointLights(renderContext);
                sceneResources.AddRange(lights);

                renderer.Render(
                    renderContext,
                    cameraResource,
                    objects,
                    lights,
                    new Color(255, 25, 25, 30),
                    Colors.White,
                    0.08f);

                return renderer.DownloadPixels();
            }
            finally
            {
                DisposeAll(objects, sceneResources, renderer);
            }
        });

        AssertLitFramebuffer(pixels);
    }

    [Test]
    public void RenderDirectionalLightShadowScene_ProducesLitNonUniformFramebuffer()
    {
        void Configure(List<Light3D> lights)
        {
            var light = new DirectionalLight3D();
            light.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -2, -1));
            light.Color.CurrentValue = Colors.White;
            light.Intensity.CurrentValue = 2.0f;
            light.IsEnabled = true;
            light.CastsShadow.CurrentValue = true;
            light.ShadowBias.CurrentValue = 0.005f;
            light.ShadowNormalBias.CurrentValue = 0.02f;
            light.ShadowStrength.CurrentValue = 1.0f;
            light.ShadowDistance.CurrentValue = 30f;
            light.ShadowMapSize.CurrentValue = 15f;
            lights.Add(light);
        }

        byte[] shadowed = RenderShadowScene(Configure);
        byte[] unshadowed = RenderShadowScene(Configure, castShadows: false);

        AssertLitFramebuffer(shadowed);
        AssertShadowsDarkenReceiver(shadowed, unshadowed);
    }

    [Test]
    public void RenderPointLightShadowScene_ProducesLitNonUniformFramebuffer()
    {
        void Configure(List<Light3D> lights)
        {
            var pointLight = new PointLight3D();
            pointLight.Position.CurrentValue = new Vector3(0, 3, 2);
            pointLight.Color.CurrentValue = new Color(255, 255, 240, 200);
            pointLight.Intensity.CurrentValue = 15f;
            pointLight.Range.CurrentValue = 15f;
            pointLight.IsEnabled = true;
            pointLight.CastsShadow.CurrentValue = true;
            pointLight.ShadowBias.CurrentValue = 0.01f;
            pointLight.ShadowStrength.CurrentValue = 0.9f;
            lights.Add(pointLight);

            var ambientFill = new DirectionalLight3D();
            ambientFill.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, -1, 0));
            ambientFill.Color.CurrentValue = new Color(255, 100, 120, 150);
            ambientFill.Intensity.CurrentValue = 0.3f;
            ambientFill.IsEnabled = true;
            ambientFill.CastsShadow.CurrentValue = false;
            lights.Add(ambientFill);
        }

        byte[] shadowed = RenderShadowScene(Configure);
        byte[] unshadowed = RenderShadowScene(Configure, castShadows: false);

        AssertLitFramebuffer(shadowed);
        AssertShadowsDarkenReceiver(shadowed, unshadowed);
    }

    [Test]
    public void RenderSpotLightShadowScene_ProducesLitNonUniformFramebuffer()
    {
        void Configure(List<Light3D> lights)
        {
            var spotLight = new SpotLight3D();
            spotLight.Position.CurrentValue = new Vector3(0, 6, 0);
            spotLight.Direction.CurrentValue = new Vector3(0, -1, 0);
            spotLight.Color.CurrentValue = new Color(255, 255, 255, 220);
            spotLight.Intensity.CurrentValue = 30f;
            spotLight.Range.CurrentValue = 15f;
            spotLight.InnerConeAngle.CurrentValue = 25f;
            spotLight.OuterConeAngle.CurrentValue = 50f;
            spotLight.IsEnabled = true;
            spotLight.CastsShadow.CurrentValue = true;
            spotLight.ShadowBias.CurrentValue = 0.005f;
            spotLight.ShadowStrength.CurrentValue = 0.95f;
            lights.Add(spotLight);

            var ambientFill = new DirectionalLight3D();
            ambientFill.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, -1, 0));
            ambientFill.Color.CurrentValue = new Color(255, 100, 120, 150);
            ambientFill.Intensity.CurrentValue = 0.3f;
            ambientFill.IsEnabled = true;
            ambientFill.CastsShadow.CurrentValue = false;
            lights.Add(ambientFill);
        }

        byte[] shadowed = RenderShadowScene(Configure);
        byte[] unshadowed = RenderShadowScene(Configure, castShadows: false);

        AssertLitFramebuffer(shadowed);
        AssertShadowsDarkenReceiver(shadowed, unshadowed);
    }

    [Test]
    public void RenderMultipleLightShadowScene_ProducesLitNonUniformFramebuffer()
    {
        void Configure(List<Light3D> lights)
        {
            var mainLight = new DirectionalLight3D();
            mainLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -1.5f, -0.5f));
            mainLight.Color.CurrentValue = new Color(255, 255, 250, 230);
            mainLight.Intensity.CurrentValue = 1.8f;
            mainLight.IsEnabled = true;
            mainLight.CastsShadow.CurrentValue = true;
            mainLight.ShadowStrength.CurrentValue = 0.7f;
            lights.Add(mainLight);

            var secondarySpot = new SpotLight3D();
            secondarySpot.Position.CurrentValue = new Vector3(-3, 4, 3);
            secondarySpot.Direction.CurrentValue = Vector3.Normalize(new Vector3(0.3f, -1, -0.3f));
            secondarySpot.Color.CurrentValue = new Color(255, 200, 230, 255);
            secondarySpot.Intensity.CurrentValue = 12f;
            secondarySpot.Range.CurrentValue = 15f;
            secondarySpot.InnerConeAngle.CurrentValue = 20f;
            secondarySpot.OuterConeAngle.CurrentValue = 40f;
            secondarySpot.IsEnabled = true;
            secondarySpot.CastsShadow.CurrentValue = true;
            secondarySpot.ShadowStrength.CurrentValue = 0.6f;
            lights.Add(secondarySpot);
        }

        byte[] shadowed = RenderShadowScene(Configure);
        byte[] unshadowed = RenderShadowScene(Configure, castShadows: false);

        AssertLitFramebuffer(shadowed);
        AssertShadowsDarkenReceiver(shadowed, unshadowed);
    }

    private static List<Light3D.Resource> BuildThreePointLights(CompositionContext renderContext)
    {
        var keyLight = new DirectionalLight3D();
        keyLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -1, -1));
        keyLight.Color.CurrentValue = Colors.White;
        keyLight.Intensity.CurrentValue = 2.5f;
        keyLight.IsEnabled = true;

        var fillLight = new DirectionalLight3D();
        fillLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0.8f, -0.3f, -0.5f));
        fillLight.Color.CurrentValue = new Color(255, 220, 200, 255);
        fillLight.Intensity.CurrentValue = 1.0f;
        fillLight.IsEnabled = true;

        var rimLight = new DirectionalLight3D();
        rimLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, 0, 1));
        rimLight.Color.CurrentValue = new Color(255, 180, 200, 255);
        rimLight.Intensity.CurrentValue = 0.6f;
        rimLight.IsEnabled = true;

        return new List<Light3D.Resource>
        {
            (DirectionalLight3D.Resource)keyLight.ToResource(renderContext),
            (DirectionalLight3D.Resource)fillLight.ToResource(renderContext),
            (DirectionalLight3D.Resource)rimLight.ToResource(renderContext),
        };
    }

    /// <summary>
    /// Renders the shared shadow scene (ground plane + three shadow-casting spheres) with a
    /// caller-supplied light rig, and returns the downloaded RGBA16Float framebuffer.
    /// </summary>
    private byte[] RenderShadowScene(Action<List<Light3D>> configureLights, bool castShadows = true)
    {
        return GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            var renderer = new Renderer3D(_context);
            var objects = new List<Object3D.Resource>();
            var sceneResources = new List<IDisposable>();
            try
            {
                renderer.Initialize(Width, Height);

                var renderContext = new CompositionContext(TimeSpan.Zero);

                var camera = new PerspectiveCamera();
                camera.Position.CurrentValue = new Vector3(8, 6, 8);
                camera.Target.CurrentValue = new Vector3(0, 0, 0);
                camera.Up.CurrentValue = Vector3.UnitY;
                camera.FieldOfView.CurrentValue = 50f;
                camera.NearPlane.CurrentValue = 0.1f;
                camera.FarPlane.CurrentValue = 100f;
                var cameraResource = (PerspectiveCamera.Resource)camera.ToResource(renderContext);
                sceneResources.Add(cameraResource);

                var ground = new Cube3D();
                ground.Width.CurrentValue = 20f;
                ground.Height.CurrentValue = 0.1f;
                ground.Depth.CurrentValue = 20f;
                ground.Position.CurrentValue = new Vector3(0, -1.05f, 0);
                ground.ReceiveShadows.CurrentValue = true;
                var groundMaterial = new PBRMaterial();
                groundMaterial.Albedo.CurrentValue = new Color(255, 180, 180, 180);
                groundMaterial.Roughness.CurrentValue = 0.8f;
                groundMaterial.Metallic.CurrentValue = 0.0f;
                ground.Material.CurrentValue = groundMaterial;
                objects.Add((Cube3D.Resource)ground.ToResource(renderContext));

                objects.Add(CreateShadowSphere(renderContext, new Vector3(0, 0.5f, 0), 1.0f, new Color(255, 220, 80, 80), 0.3f, 0.1f));
                objects.Add(CreateShadowSphere(renderContext, new Vector3(-3, 0.3f, 1), 0.7f, new Color(255, 80, 200, 80), 0.5f, 0.8f));
                objects.Add(CreateShadowSphere(renderContext, new Vector3(2.5f, 0.4f, -1), 0.8f, new Color(255, 80, 120, 220), 0.2f, 0.9f));

                var lightModels = new List<Light3D>();
                configureLights(lightModels);
                if (!castShadows)
                {
                    // Re-render the identical rig with shadow casting forced off so the caller can diff the
                    // two frames; that diff is what gates that shadows actually darken the receiver.
                    foreach (Light3D light in lightModels)
                    {
                        light.CastsShadow.CurrentValue = false;
                    }
                }
                var lights = lightModels
                    .Select(l => (Light3D.Resource)l.ToResource(renderContext))
                    .ToList();
                sceneResources.AddRange(lights);

                renderer.Render(
                    renderContext,
                    cameraResource,
                    objects,
                    lights,
                    new Color(255, 40, 50, 60),
                    Colors.White,
                    0.15f);

                return renderer.DownloadPixels();
            }
            finally
            {
                DisposeAll(objects, sceneResources, renderer);
            }
        });
    }

    private static Sphere3D.Resource CreateShadowSphere(
        CompositionContext renderContext, Vector3 position, float radius, Color albedo, float roughness, float metallic)
    {
        var sphere = new Sphere3D();
        sphere.Position.CurrentValue = position;
        sphere.Radius.CurrentValue = radius;
        sphere.Segments.CurrentValue = 32;
        sphere.Rings.CurrentValue = 16;
        sphere.CastShadows.CurrentValue = true;

        var material = new PBRMaterial();
        material.Albedo.CurrentValue = albedo;
        material.Roughness.CurrentValue = roughness;
        material.Metallic.CurrentValue = metallic;
        sphere.Material.CurrentValue = material;

        return (Sphere3D.Resource)sphere.ToResource(renderContext);
    }

    /// <summary>
    /// Releases every per-render resource the test owns. The camera/light/object resources created via
    /// <c>ToResource</c> are not owned by <see cref="Renderer3D"/>, so a failed render must not leak them
    /// into later tests that share the same graphics context.
    /// </summary>
    private static void DisposeAll(
        List<Object3D.Resource> objects, List<IDisposable> sceneResources, Renderer3D renderer)
    {
        foreach (var obj in objects)
        {
            obj.Dispose();
        }

        foreach (IDisposable res in sceneResources)
        {
            res.Dispose();
        }

        renderer.Dispose();
    }

    /// <summary>
    /// Asserts that the downloaded RGBA16Float framebuffer is non-empty, correctly sized, and
    /// contains lit, non-uniform content (i.e. the scene actually rendered, not a blank buffer).
    /// </summary>
    private static void AssertLitFramebuffer(byte[] pixels)
    {
        const int bytesPerPixel = 8; // 4 × Half (RGBA16Float)
        int pixelCount = Width * Height;

        Assert.That(pixels, Is.Not.Null);
        Assert.That(pixels.Length, Is.EqualTo(pixelCount * bytesPerPixel),
            "Framebuffer must be the full RGBA16Float surface.");

        float maxLuma = 0f;
        long litPixels = 0;
        bool sawVariation = false;
        float firstLuma = float.NaN;

        for (int i = 0; i < pixelCount; i++)
        {
            int off = i * bytesPerPixel;
            float luma = Luma(pixels, off);
            if (luma > maxLuma) maxLuma = luma;

            // Above the dark clear color: counts as a "lit" (foreground/shaded) pixel.
            if (luma > 0.05f) litPixels++;

            if (float.IsNaN(firstLuma))
            {
                firstLuma = luma;
            }
            else if (!sawVariation && MathF.Abs(luma - firstLuma) > 0.01f)
            {
                sawVariation = true;
            }
        }

        // A real PBR-grid / multi-light render brightly lights well over 10% of the surface and peaks
        // far above the dark clear color. Discriminate it from a corrupt frame (mostly-black with a few
        // stray lit/noise pixels) by requiring a meaningful lit FRACTION and a high peak luma, not just
        // a single lit pixel or any two pixels differing.
        Assert.That(maxLuma, Is.GreaterThan(0.5f),
            $"Framebuffer peak luma {maxLuma:F3} is too dark: nothing appears to have been brightly lit/rendered.");
        Assert.That(litPixels, Is.GreaterThan(pixelCount / 10),
            $"Only {litPixels} of {pixelCount} pixels are above background (< 10%): the rendered geometry "
            + "does not cover enough of the surface to be a real scene.");
        Assert.That(sawVariation, Is.True,
            "Framebuffer is uniform: the scene did not render distinct geometry against the background.");
    }

    /// <summary>
    /// Asserts that enabling shadow casting darkens a meaningful region of the receiver: the
    /// shadow-enabled frame must be visibly darker than the same scene rendered with
    /// <see cref="Light3D.CastsShadow"/> forced off. Without this, a regression that ignores
    /// CastsShadow or never samples the shadow map would still produce a lit, non-uniform frame and
    /// pass <see cref="AssertLitFramebuffer"/>.
    /// </summary>
    private static void AssertShadowsDarkenReceiver(byte[] shadowed, byte[] unshadowed)
    {
        const int bytesPerPixel = 8; // 4 × Half (RGBA16Float)
        int pixelCount = Width * Height;

        Assert.That(shadowed.Length, Is.EqualTo(unshadowed.Length),
            "Shadowed and shadow-disabled frames must be the same surface size to diff.");

        long darkenedPixels = 0;
        double totalLumaDelta = 0;

        for (int i = 0; i < pixelCount; i++)
        {
            int off = i * bytesPerPixel;
            float delta = Luma(unshadowed, off) - Luma(shadowed, off); // positive => shadow darkened it
            totalLumaDelta += delta;
            if (delta > 0.05f) darkenedPixels++;
        }

        // A working shadow map darkens a contiguous receiver region (the ground under the spheres) —
        // thousands of pixels here — whereas a scene that ignores shadows differs only by GPU noise.
        // Require both a darker aggregate and a sizeable darkened area to separate the two.
        Assert.That(totalLumaDelta, Is.GreaterThan(0.0),
            "The shadow-enabled frame is not darker overall than the shadow-disabled frame: "
            + "shadow casting appears to have no effect.");
        Assert.That(darkenedPixels, Is.GreaterThan(pixelCount / 500),
            $"Only {darkenedPixels} of {pixelCount} pixels were darkened by enabling shadows: the shadow map "
            + "appears to be ignored or never sampled.");
    }

    private static float Luma(byte[] pixels, int byteOffset)
    {
        float r = (float)BitConverter.ToHalf(pixels, byteOffset);
        float g = (float)BitConverter.ToHalf(pixels, byteOffset + 2);
        float b = (float)BitConverter.ToHalf(pixels, byteOffset + 4);
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }
}
