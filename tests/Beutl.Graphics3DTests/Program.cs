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

Console.WriteLine("=== Beutl 3D Graphics Test ===");
Console.WriteLine();

// Initialize graphics context using the abstraction layer
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
const int Width = 800;
const int Height = 600;
const string BasicOutputPath = "render_basic.png";
const string PBROutputPath = "render_pbr.png";

Console.WriteLine($"Render size: {Width}x{Height}");
Console.WriteLine();

// Create 3D renderer via the abstracted interface
Console.WriteLine("Creating 3D renderer...");
var renderer = graphicsContext.Create3DRenderer();
renderer.Initialize(Width, Height);
Console.WriteLine("Renderer initialized.");
Console.WriteLine();

// Create camera
Console.WriteLine("Setting up scene...");
var camera = new PerspectiveCamera();
camera.Position.CurrentValue = new Vector3(3, 3, 5);
camera.Target.CurrentValue = Vector3.Zero;
camera.Up.CurrentValue = Vector3.UnitY;
camera.FieldOfView.CurrentValue = 60f;
camera.NearPlane.CurrentValue = 0.1f;
camera.FarPlane.CurrentValue = 100f;

// Create camera resource
var renderContext = new RenderContext(TimeSpan.Zero);
var cameraResource = (PerspectiveCamera.Resource)camera.ToResource(renderContext);

// === Test 1: BasicMaterial with DirectionalLight ===
Console.WriteLine("--- Test 1: BasicMaterial with DirectionalLight ---");

var cube1 = new Cube3D();
cube1.Position.CurrentValue = Vector3.Zero;
cube1.Scale.CurrentValue = Vector3.One;
cube1.Rotation.CurrentValue = Quaternion.CreateFromYawPitchRoll(MathF.PI / 6, MathF.PI / 8, 0);
cube1.Width.CurrentValue = 2f;
cube1.Height.CurrentValue = 2f;
cube1.Depth.CurrentValue = 2f;

var basicMaterial = new BasicMaterial();
basicMaterial.DiffuseColor.CurrentValue = new Color(255, 100, 150, 200); // Light blue-ish
cube1.Material.CurrentValue = basicMaterial;

var cube1Resource = (Cube3D.Resource)cube1.ToResource(renderContext);

// Create directional light
var dirLight = new DirectionalLight3D();
dirLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -1, -1));
dirLight.Color.CurrentValue = Colors.White;
dirLight.Intensity.CurrentValue = 1.0f;
dirLight.IsLightEnabled.CurrentValue = true;

var dirLightResource = (DirectionalLight3D.Resource)dirLight.ToResource(renderContext);

Console.WriteLine("  Camera: PerspectiveCamera at (3, 3, 5) looking at origin");
Console.WriteLine("  Object: Cube3D (2x2x2) with BasicMaterial");
Console.WriteLine("  Light: DirectionalLight3D from (-1, -1, -1)");
Console.WriteLine();

// Render BasicMaterial test
Console.WriteLine("Rendering BasicMaterial test...");
var objects1 = new List<Object3D.Resource> { cube1Resource };
var lights1 = new List<Light3D.Resource> { dirLightResource };

renderer.Render(
    cameraResource,
    objects1,
    lights1,
    new Color(255, 30, 30, 50), // Dark blue background
    Colors.White, // Ambient color
    0.2f); // Ambient intensity

SaveRenderOutput(renderer, Width, Height, BasicOutputPath);
Console.WriteLine($"Output saved to: {Path.GetFullPath(BasicOutputPath)}");
Console.WriteLine();

// === Test 2: PBRMaterial with Multiple Lights ===
Console.WriteLine("--- Test 2: PBRMaterial with Multiple Lights ---");

var cube2 = new Cube3D();
cube2.Position.CurrentValue = Vector3.Zero;
cube2.Scale.CurrentValue = Vector3.One;
cube2.Rotation.CurrentValue = Quaternion.CreateFromYawPitchRoll(MathF.PI / 6, MathF.PI / 8, 0);
cube2.Width.CurrentValue = 2f;
cube2.Height.CurrentValue = 2f;
cube2.Depth.CurrentValue = 2f;

var pbrMaterial = new PBRMaterial();
pbrMaterial.Albedo.CurrentValue = new Color(255, 180, 120, 80); // Gold-ish
pbrMaterial.Metallic.CurrentValue = 0.8f; // Mostly metallic
pbrMaterial.Roughness.CurrentValue = 0.3f; // Somewhat smooth
pbrMaterial.AmbientOcclusion.CurrentValue = 1.0f;
cube2.Material.CurrentValue = pbrMaterial;

var cube2Resource = (Cube3D.Resource)cube2.ToResource(renderContext);

// Create multiple lights
// Directional light (sun)
var sunLight = new DirectionalLight3D();
sunLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(0, -1, -0.5f));
sunLight.Color.CurrentValue = new Color(255, 255, 240, 220); // Warm white
sunLight.Intensity.CurrentValue = 0.8f;
sunLight.IsLightEnabled.CurrentValue = true;

var sunLightResource = (DirectionalLight3D.Resource)sunLight.ToResource(renderContext);

// Point light (red)
var pointLight = new PointLight3D();
pointLight.Position.CurrentValue = new Vector3(3, 2, 0);
pointLight.Color.CurrentValue = new Color(255, 255, 100, 100); // Red
pointLight.Intensity.CurrentValue = 1.5f;
pointLight.Range.CurrentValue = 10f;
pointLight.ConstantAttenuation.CurrentValue = 1.0f;
pointLight.LinearAttenuation.CurrentValue = 0.09f;
pointLight.QuadraticAttenuation.CurrentValue = 0.032f;
pointLight.IsLightEnabled.CurrentValue = true;

var pointLightResource = (PointLight3D.Resource)pointLight.ToResource(renderContext);

// Spot light (blue)
var spotLight = new SpotLight3D();
spotLight.Position.CurrentValue = new Vector3(-3, 3, 2);
spotLight.Direction.CurrentValue = Vector3.Normalize(new Vector3(1, -1, -0.5f));
spotLight.Color.CurrentValue = new Color(255, 100, 150, 255); // Blue
spotLight.Intensity.CurrentValue = 2.0f;
spotLight.InnerConeAngle.CurrentValue = 15f;
spotLight.OuterConeAngle.CurrentValue = 25f;
spotLight.Range.CurrentValue = 15f;
spotLight.IsLightEnabled.CurrentValue = true;

var spotLightResource = (SpotLight3D.Resource)spotLight.ToResource(renderContext);

Console.WriteLine("  Object: Cube3D (2x2x2) with PBRMaterial (Gold, Metallic=0.8, Roughness=0.3)");
Console.WriteLine("  Lights:");
Console.WriteLine("    - DirectionalLight3D (warm white sun)");
Console.WriteLine("    - PointLight3D (red) at (3, 2, 0)");
Console.WriteLine("    - SpotLight3D (blue) at (-3, 3, 2)");
Console.WriteLine();

// Render PBR test
Console.WriteLine("Rendering PBR test...");
var objects2 = new List<Object3D.Resource> { cube2Resource };
var lights2 = new List<Light3D.Resource> { sunLightResource, pointLightResource, spotLightResource };

renderer.Render(
    cameraResource,
    objects2,
    lights2,
    new Color(255, 20, 20, 30), // Dark background
    Colors.White, // Ambient color
    0.05f); // Low ambient intensity for PBR

SaveRenderOutput(renderer, Width, Height, PBROutputPath);
Console.WriteLine($"Output saved to: {Path.GetFullPath(PBROutputPath)}");
Console.WriteLine();

// Cleanup
Console.WriteLine("Cleaning up...");
cube1Resource.Dispose();
cube2Resource.Dispose();
renderer.Dispose();
graphicsContext.Dispose();

Console.WriteLine("Done!");
Console.WriteLine();
Console.WriteLine("=== Test Complete ===");

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
