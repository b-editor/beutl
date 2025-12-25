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
var renderer = graphicsContext.Create3DRenderer();
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
keyLight.IsLightEnabled.CurrentValue = true;

var fillLight = new DirectionalLight3D();
fillLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0.8f, -0.3f, -0.5f));
fillLight.Color.CurrentValue = new Color(255, 220, 200, 255); // Warm
fillLight.Intensity.CurrentValue = 1.0f;
fillLight.IsLightEnabled.CurrentValue = true;

var rimLight = new DirectionalLight3D();
rimLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, 0, 1));
rimLight.Color.CurrentValue = new Color(255, 180, 200, 255); // Cool
rimLight.Intensity.CurrentValue = 0.6f;
rimLight.IsLightEnabled.CurrentValue = true;

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

// Cleanup
Console.WriteLine("Cleaning up...");
foreach (var obj in objects)
{
    obj.Dispose();
}
renderer.Dispose();
graphicsContext.Dispose();

Console.WriteLine("Done!");
return 0;

static void SaveRenderOutput(I3DRenderer renderer, int width, int height, string outputPath)
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
