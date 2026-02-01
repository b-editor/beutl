using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using SkiaSharp;

Console.WriteLine("=== Beutl 3D Graphics Test - PBR Material Grid ===");
Console.WriteLine();

// Initialize graphics context
Console.WriteLine("Initializing graphics context...");
var graphicsContext = GraphicsContextFactory.CreateContext();

Console.WriteLine($"Backend: {graphicsContext.Backend}");
Console.WriteLine($"Supports 3D Rendering: {graphicsContext.Supports3DRendering}");
Console.WriteLine();

if (!graphicsContext.Supports3DRendering)
{
    Console.WriteLine("3D rendering is not supported on this system.");
    return 1;
}

// Render settings
const int Width = 1200;
const int Height = 800;
const string PBRGridOutputPath = "render_pbr_grid.png";

Console.WriteLine($"Render size: {Width}x{Height}");
Console.WriteLine();

// Create 3D renderer
Console.WriteLine("Creating 3D renderer...");
var renderer = new Renderer3D(graphicsContext);
renderer.Initialize(Width, Height);
Console.WriteLine("Renderer initialized.");
Console.WriteLine();

var renderContext = new RenderContext(TimeSpan.Zero);

// === PBR Material Grid Test ===
Console.WriteLine("=== PBR Material Grid Test ===");
Console.WriteLine();
Console.WriteLine("Grid layout:");
Console.WriteLine("  Horizontal (left to right): Metallic 0.0 -> 1.0");
Console.WriteLine("  Vertical (bottom to top):   Roughness 0.0 -> 1.0");
Console.WriteLine();

// Camera
var camera = new PerspectiveCamera();
camera.Position.CurrentValue = new Vector3(0, 0, 12);
camera.Target.CurrentValue = new Vector3(0, 0, 0);
camera.Up.CurrentValue = Vector3.UnitY;
camera.FieldOfView.CurrentValue = 45f;
camera.NearPlane.CurrentValue = 0.1f;
camera.FarPlane.CurrentValue = 100f;

var cameraResource = (PerspectiveCamera.Resource)camera.ToResource(renderContext);

// Grid parameters
const int GridSizeX = 7; // Metallic steps
const int GridSizeY = 7; // Roughness steps
const float SphereRadius = 0.4f;
const float Spacing = 1.1f;

var objects = new List<Object3D.Resource>();

// Calculate grid offset to center
float offsetX = -(GridSizeX - 1) * Spacing * 0.5f;
float offsetY = -(GridSizeY - 1) * Spacing * 0.5f;

Console.WriteLine($"Creating {GridSizeX}x{GridSizeY} = {GridSizeX * GridSizeY} spheres...");

for (int y = 0; y < GridSizeY; y++)
{
    float roughness = (float)y / (GridSizeY - 1); // 0.0 to 1.0

    for (int x = 0; x < GridSizeX; x++)
    {
        float metallic = (float)x / (GridSizeX - 1); // 0.0 to 1.0

        var sphere = new Sphere3D();
        sphere.Position.CurrentValue = new Vector3(
            offsetX + x * Spacing,
            offsetY + y * Spacing,
            0);
        sphere.Radius.CurrentValue = SphereRadius;
        sphere.Segments.CurrentValue = 32;
        sphere.Rings.CurrentValue = 16;

        // PBR material with bright gold color for visibility
        var material = new PBRMaterial();
        material.Albedo.CurrentValue = new Color(255, 255, 200, 50); // Bright orange-gold
        material.Metallic.CurrentValue = metallic;
        material.Roughness.CurrentValue = roughness;
        material.AmbientOcclusion.CurrentValue = 1.0f;
        sphere.Material.CurrentValue = material;

        objects.Add((Sphere3D.Resource)sphere.ToResource(renderContext));
    }
}

Console.WriteLine("Spheres created.");
Console.WriteLine();

// Lighting - 3-point lighting setup
var keyLight = new DirectionalLight3D();
keyLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -1, -1));
keyLight.Color.CurrentValue = Colors.White;
keyLight.Intensity.CurrentValue = 2.5f;
keyLight.IsEnabled = true;

var fillLight = new DirectionalLight3D();
fillLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0.8f, -0.3f, -0.5f));
fillLight.Color.CurrentValue = new Color(255, 220, 200, 255); // Warm
fillLight.Intensity.CurrentValue = 1.0f;
fillLight.IsEnabled = true;

var rimLight = new DirectionalLight3D();
rimLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, 0, 1));
rimLight.Color.CurrentValue = new Color(255, 180, 200, 255); // Cool
rimLight.Intensity.CurrentValue = 0.6f;
rimLight.IsEnabled = true;

var lights = new List<Light3D.Resource>
{
    (DirectionalLight3D.Resource)keyLight.ToResource(renderContext),
    (DirectionalLight3D.Resource)fillLight.ToResource(renderContext),
    (DirectionalLight3D.Resource)rimLight.ToResource(renderContext)
};

Console.WriteLine("Lights: Key (white), Fill (warm), Rim (cool)");
Console.WriteLine();

// Render
Console.WriteLine("Rendering PBR grid...");
renderer.Render(
    renderContext,
    cameraResource,
    objects,
    lights,
    new Color(255, 25, 25, 30), // Dark background
    Colors.White,
    0.08f); // Low ambient

SaveRenderOutput(renderer, Width, Height, PBRGridOutputPath);
Console.WriteLine($"Output saved to: {Path.GetFullPath(PBRGridOutputPath)}");
Console.WriteLine();

// Legend
Console.WriteLine("=== What to observe ===");
Console.WriteLine("Bottom-left (Metallic=0, Roughness=0): Smooth plastic - sharp WHITE specular");
Console.WriteLine("Bottom-right (Metallic=1, Roughness=0): Polished metal - sharp COLORED specular");
Console.WriteLine("Top-left (Metallic=0, Roughness=1): Matte plastic - soft diffuse");
Console.WriteLine("Top-right (Metallic=1, Roughness=1): Rough metal - soft colored highlights");
Console.WriteLine();
Console.WriteLine("Key differences:");
Console.WriteLine("- Dielectric (left): White specular highlights, visible base color");
Console.WriteLine("- Metal (right): Colored specular (reflects albedo), darker diffuse");
Console.WriteLine("- Smooth (bottom): Sharp, concentrated highlights");
Console.WriteLine("- Rough (top): Spread-out, soft lighting");
Console.WriteLine();

// Cleanup PBR grid test
Console.WriteLine("Cleaning up PBR grid test...");
foreach (var obj in objects)
{
    obj.Dispose();
}
renderer.Dispose();

// Run shadow tests
RunShadowTest(graphicsContext);

// Final cleanup
Console.WriteLine("Final cleanup...");
graphicsContext.Dispose();

Console.WriteLine("All tests complete!");
return 0;

static void SaveRenderOutput(IRenderer3D renderer, int width, int height, string outputPath)
{
    var pixelData = renderer.DownloadPixels();
    using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
    unsafe
    {
        fixed (byte* ptr = pixelData)
        {
            bitmap.SetPixels((IntPtr)ptr);
        }
    }
    using var image = SKImage.FromBitmap(bitmap);
    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
    using var stream = File.OpenWrite(outputPath);
    data.SaveTo(stream);
}

// === Shadow Test Scene ===
static void RunShadowTest(IGraphicsContext graphicsContext)
{
    Console.WriteLine();
    Console.WriteLine("=".PadRight(60, '='));
    Console.WriteLine("=== Shadow Mapping Test ===");
    Console.WriteLine("=".PadRight(60, '='));
    Console.WriteLine();

    const int Width = 1200;
    const int Height = 800;

    // Create 3D renderer
    Console.WriteLine("Creating 3D renderer for shadow test...");
    var renderer = new Renderer3D(graphicsContext);
    renderer.Initialize(Width, Height);

    var renderContext = new RenderContext(TimeSpan.Zero);

    // Camera - view from above and to the side
    var camera = new PerspectiveCamera();
    camera.Position.CurrentValue = new Vector3(8, 6, 8);
    camera.Target.CurrentValue = new Vector3(0, 0, 0);
    camera.Up.CurrentValue = Vector3.UnitY;
    camera.FieldOfView.CurrentValue = 50f;
    camera.NearPlane.CurrentValue = 0.1f;
    camera.FarPlane.CurrentValue = 100f;

    var cameraResource = (PerspectiveCamera.Resource)camera.ToResource(renderContext);

    var objects = new List<Object3D.Resource>();
    var lights = new List<Light3D.Resource>();

    // --- Create Ground Plane (using flat cube) ---
    Console.WriteLine("Creating ground plane...");
    var ground = new Cube3D();
    ground.Width.CurrentValue = 20f;
    ground.Height.CurrentValue = 0.1f;  // Very thin to simulate a plane
    ground.Depth.CurrentValue = 20f;
    ground.Position.CurrentValue = new Vector3(0, -1.05f, 0);
    ground.ReceiveShadows.CurrentValue = true;

    var groundMaterial = new PBRMaterial();
    groundMaterial.Albedo.CurrentValue = new Color(255, 180, 180, 180); // Light gray
    groundMaterial.Roughness.CurrentValue = 0.8f;
    groundMaterial.Metallic.CurrentValue = 0.0f;
    ground.Material.CurrentValue = groundMaterial;

    objects.Add((Cube3D.Resource)ground.ToResource(renderContext));

    // --- Create Shadow-casting Objects ---
    Console.WriteLine("Creating shadow-casting objects...");

    // Sphere 1 - center
    var sphere1 = new Sphere3D();
    sphere1.Position.CurrentValue = new Vector3(0, 0.5f, 0);
    sphere1.Radius.CurrentValue = 1.0f;
    sphere1.Segments.CurrentValue = 32;
    sphere1.Rings.CurrentValue = 16;
    sphere1.CastShadows.CurrentValue = true;

    var sphere1Material = new PBRMaterial();
    sphere1Material.Albedo.CurrentValue = new Color(255, 220, 80, 80); // Red
    sphere1Material.Roughness.CurrentValue = 0.3f;
    sphere1Material.Metallic.CurrentValue = 0.1f;
    sphere1.Material.CurrentValue = sphere1Material;

    objects.Add((Sphere3D.Resource)sphere1.ToResource(renderContext));

    // Sphere 2 - left
    var sphere2 = new Sphere3D();
    sphere2.Position.CurrentValue = new Vector3(-3, 0.3f, 1);
    sphere2.Radius.CurrentValue = 0.7f;
    sphere2.Segments.CurrentValue = 32;
    sphere2.Rings.CurrentValue = 16;
    sphere2.CastShadows.CurrentValue = true;

    var sphere2Material = new PBRMaterial();
    sphere2Material.Albedo.CurrentValue = new Color(255, 80, 200, 80); // Green
    sphere2Material.Roughness.CurrentValue = 0.5f;
    sphere2Material.Metallic.CurrentValue = 0.8f;
    sphere2.Material.CurrentValue = sphere2Material;

    objects.Add((Sphere3D.Resource)sphere2.ToResource(renderContext));

    // Sphere 3 - right
    var sphere3 = new Sphere3D();
    sphere3.Position.CurrentValue = new Vector3(2.5f, 0.4f, -1);
    sphere3.Radius.CurrentValue = 0.8f;
    sphere3.Segments.CurrentValue = 32;
    sphere3.Rings.CurrentValue = 16;
    sphere3.CastShadows.CurrentValue = true;

    var sphere3Material = new PBRMaterial();
    sphere3Material.Albedo.CurrentValue = new Color(255, 80, 120, 220); // Blue
    sphere3Material.Roughness.CurrentValue = 0.2f;
    sphere3Material.Metallic.CurrentValue = 0.9f;
    sphere3.Material.CurrentValue = sphere3Material;

    objects.Add((Sphere3D.Resource)sphere3.ToResource(renderContext));

    // === Test 1: Directional Light Shadow ===
    Console.WriteLine();
    Console.WriteLine("--- Test 1: Directional Light Shadow ---");

    lights.Clear();

    var directionalLight = new DirectionalLight3D();
    directionalLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -2, -1));
    directionalLight.Color.CurrentValue = Colors.White;
    directionalLight.Intensity.CurrentValue = 2.0f;
    directionalLight.IsEnabled = true;
    directionalLight.CastsShadow.CurrentValue = true;
    directionalLight.ShadowBias.CurrentValue = 0.005f;
    directionalLight.ShadowNormalBias.CurrentValue = 0.02f;
    directionalLight.ShadowStrength.CurrentValue = 1.0f;
    directionalLight.ShadowDistance.CurrentValue = 30f;
    directionalLight.ShadowMapSize.CurrentValue = 15f;

    lights.Add((DirectionalLight3D.Resource)directionalLight.ToResource(renderContext));

    Console.WriteLine($"  Light direction: {directionalLight.Direction.CurrentValue}");
    Console.WriteLine($"  Shadow enabled: {directionalLight.CastsShadow.CurrentValue}");
    Console.WriteLine($"  Shadow bias: {directionalLight.ShadowBias.CurrentValue}");

    Console.WriteLine("  Rendering...");
    renderer.Render(
        renderContext,
        cameraResource,
        objects,
        lights,
        new Color(255, 40, 50, 60),
        Colors.White,
        0.15f);

    SaveRenderOutput(renderer, Width, Height, "shadow_directional.png");
    Console.WriteLine($"  Saved to: {Path.GetFullPath("shadow_directional.png")}");

    // === Test 2: Point Light Shadow ===
    Console.WriteLine();
    Console.WriteLine("--- Test 2: Point Light Shadow ---");

    lights.Clear();

    var pointLight = new PointLight3D();
    pointLight.Position.CurrentValue = new Vector3(0, 3, 2);
    pointLight.Color.CurrentValue = new Color(255, 255, 240, 200); // Warm white
    pointLight.Intensity.CurrentValue = 15f;
    pointLight.Range.CurrentValue = 15f;
    pointLight.IsEnabled = true;
    pointLight.CastsShadow.CurrentValue = true;
    pointLight.ShadowBias.CurrentValue = 0.01f;
    pointLight.ShadowStrength.CurrentValue = 0.9f;

    lights.Add((PointLight3D.Resource)pointLight.ToResource(renderContext));

    // Add dim ambient fill
    var ambientFill = new DirectionalLight3D();
    ambientFill.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, -1, 0));
    ambientFill.Color.CurrentValue = new Color(255, 100, 120, 150);
    ambientFill.Intensity.CurrentValue = 0.3f;
    ambientFill.IsEnabled = true;
    ambientFill.CastsShadow.CurrentValue = false;

    lights.Add((DirectionalLight3D.Resource)ambientFill.ToResource(renderContext));

    Console.WriteLine($"  Light position: {pointLight.Position.CurrentValue}");
    Console.WriteLine($"  Light range: {pointLight.Range.CurrentValue}");
    Console.WriteLine($"  Shadow enabled: {pointLight.CastsShadow.CurrentValue}");

    Console.WriteLine("  Rendering...");
    renderer.Render(
        renderContext,
        cameraResource,
        objects,
        lights,
        new Color(255, 20, 25, 35),
        Colors.White,
        0.05f);

    SaveRenderOutput(renderer, Width, Height, "shadow_point.png");
    Console.WriteLine($"  Saved to: {Path.GetFullPath("shadow_point.png")}");

    // === Test 3: Spot Light Shadow ===
    Console.WriteLine();
    Console.WriteLine("--- Test 3: Spot Light Shadow ---");

    lights.Clear();

    var spotLight = new SpotLight3D();
    // Position directly above looking straight down for clear shadow testing
    spotLight.Position.CurrentValue = new Vector3(0, 6, 0);
    spotLight.Direction.CurrentValue = new Vector3(0, -1, 0);
    spotLight.Color.CurrentValue = new Color(255, 255, 255, 220);
    spotLight.Intensity.CurrentValue = 30f;
    spotLight.Range.CurrentValue = 15f;
    spotLight.InnerConeAngle.CurrentValue = 25f;
    spotLight.OuterConeAngle.CurrentValue = 50f;  // Wider cone to cover scene
    spotLight.IsEnabled = true;
    spotLight.CastsShadow.CurrentValue = true;
    spotLight.ShadowBias.CurrentValue = 0.005f;
    spotLight.ShadowStrength.CurrentValue = 0.95f;

    lights.Add((SpotLight3D.Resource)spotLight.ToResource(renderContext));

    // Add dim fill light
    lights.Add((DirectionalLight3D.Resource)ambientFill.ToResource(renderContext));

    Console.WriteLine($"  Light position: {spotLight.Position.CurrentValue}");
    Console.WriteLine($"  Light direction: {spotLight.Direction.CurrentValue}");
    Console.WriteLine($"  Cone angles: inner={spotLight.InnerConeAngle.CurrentValue}°, outer={spotLight.OuterConeAngle.CurrentValue}°");
    Console.WriteLine($"  Shadow enabled: {spotLight.CastsShadow.CurrentValue}");

    Console.WriteLine("  Rendering...");
    renderer.Render(
        renderContext,
        cameraResource,
        objects,
        lights,
        new Color(255, 15, 20, 30),
        Colors.White,
        0.03f);

    SaveRenderOutput(renderer, Width, Height, "shadow_spot.png");
    Console.WriteLine($"  Saved to: {Path.GetFullPath("shadow_spot.png")}");

    // === Test 4: Multiple Lights with Shadows ===
    Console.WriteLine();
    Console.WriteLine("--- Test 4: Multiple Lights with Shadows ---");

    lights.Clear();

    // Main directional light with shadow
    var mainLight = new DirectionalLight3D();
    mainLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -1.5f, -0.5f));
    mainLight.Color.CurrentValue = new Color(255, 255, 250, 230);
    mainLight.Intensity.CurrentValue = 1.8f;
    mainLight.IsEnabled = true;
    mainLight.CastsShadow.CurrentValue = true;
    mainLight.ShadowStrength.CurrentValue = 0.7f;

    lights.Add((DirectionalLight3D.Resource)mainLight.ToResource(renderContext));

    // Secondary spot light with shadow
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

    lights.Add((SpotLight3D.Resource)secondarySpot.ToResource(renderContext));

    Console.WriteLine("  Main directional light with shadow");
    Console.WriteLine("  Secondary spot light with shadow (bluish)");

    Console.WriteLine("  Rendering...");
    renderer.Render(
        renderContext,
        cameraResource,
        objects,
        lights,
        new Color(255, 30, 35, 45),
        Colors.White,
        0.1f);

    SaveRenderOutput(renderer, Width, Height, "shadow_multiple.png");
    Console.WriteLine($"  Saved to: {Path.GetFullPath("shadow_multiple.png")}");

    // Cleanup
    Console.WriteLine();
    Console.WriteLine("Cleaning up shadow test resources...");
    foreach (var obj in objects)
    {
        obj.Dispose();
    }
    renderer.Dispose();

    Console.WriteLine("Shadow tests complete!");
    Console.WriteLine();
}
