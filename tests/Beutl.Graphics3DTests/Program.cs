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
using Silk.NET.Vulkan;
using SkiaSharp;

Console.WriteLine("=== Beutl 3D Graphics Test ===");
Console.WriteLine();

// Initialize Vulkan
Console.WriteLine("Initializing Vulkan...");
VulkanSetup.Setup();
var vk = Vk.GetApi();
var vulkanInstance = new VulkanInstance(vk, enableValidation: false);

// Get available GPUs
var gpus = vulkanInstance.GetAvailableGpus();
Console.WriteLine($"Found {gpus.Length} GPU(s):");
foreach (var gpu in gpus)
{
    Console.WriteLine($"  - {gpu.Name} (Type: {gpu.Type})");
}
Console.WriteLine();

// Select the best GPU
var selectedGpu = vulkanInstance.SelectBestPhysicalDevice();
Console.WriteLine($"Selected GPU: {selectedGpu.Name}");
Console.WriteLine();

// Create Vulkan context
Console.WriteLine("Creating Vulkan context...");
var vulkanContext = new VulkanContext(vulkanInstance, selectedGpu);
Console.WriteLine("Vulkan context created successfully.");
Console.WriteLine();

// Render settings
const int Width = 800;
const int Height = 600;
const string OutputPath = "render_output.png";

Console.WriteLine($"Render size: {Width}x{Height}");
Console.WriteLine($"Output: {OutputPath}");
Console.WriteLine();

// Create renderer
Console.WriteLine("Creating 3D renderer...");
var renderer = new Vulkan3DRenderer(vulkanContext);
renderer.Initialize(Width, Height);

// Create shared texture for output
var colorTexture = vulkanContext.CreateTexture(Width, Height, TextureFormat.BGRA8Unorm);
renderer.Resize(Width, Height, colorTexture);
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

// Create cube
var cube = new Cube3D();
cube.Position.CurrentValue = Vector3.Zero;
cube.Scale.CurrentValue = Vector3.One;
cube.Rotation.CurrentValue = Quaternion.CreateFromYawPitchRoll(MathF.PI / 6, MathF.PI / 8, 0);
cube.Width.CurrentValue = 2f;
cube.Height.CurrentValue = 2f;
cube.Depth.CurrentValue = 2f;

var basicMaterial = new BasicMaterial();
basicMaterial.DiffuseColor.CurrentValue = new Color(255, 100, 150, 200); // Light blue-ish
cube.Material.CurrentValue = basicMaterial;

var cubeResource = (Cube3D.Resource)cube.ToResource(renderContext);
var cubeMesh = cube.GetMesh(cubeResource);

// Create directional light
var light = new DirectionalLight3D();
light.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -1, -1));
light.Color.CurrentValue = Colors.White;
light.Intensity.CurrentValue = 1.0f;
light.IsLightEnabled.CurrentValue = true;

var lightResource = (DirectionalLight3D.Resource)light.ToResource(renderContext);

Console.WriteLine("  Camera: PerspectiveCamera at (3, 3, 5) looking at origin");
Console.WriteLine("  Object: Cube3D (2x2x2) at origin");
Console.WriteLine("  Light: DirectionalLight3D from (-1, -1, -1)");
Console.WriteLine();

// Render
Console.WriteLine("Rendering...");
var objects = new List<(Object3D.Resource, Mesh)> { (cubeResource, cubeMesh) };
var lights = new List<Light3D.Resource> { lightResource };

renderer.Render(
    cameraResource,
    objects,
    lights,
    new Color(255, 30, 30, 50), // Dark blue background
    Colors.White, // Ambient color
    0.2f); // Ambient intensity

Console.WriteLine("Render complete.");
Console.WriteLine();

// Download pixel data from texture
Console.WriteLine("Downloading texture data...");
var pixelData = colorTexture.DownloadPixels();
Console.WriteLine($"Downloaded {pixelData.Length} bytes of pixel data.");

// Create SKImage from pixel data
Console.WriteLine("Creating image...");
var imageInfo = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
using var pixmap = new SKPixmap(imageInfo, System.Runtime.InteropServices.GCHandle.Alloc(pixelData, System.Runtime.InteropServices.GCHandleType.Pinned).AddrOfPinnedObject());

// Copy to a managed image to avoid pinning issues
using var bitmap = new SKBitmap(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
unsafe
{
    fixed (byte* ptr = pixelData)
    {
        bitmap.SetPixels((IntPtr)ptr);
    }
}
using var image = SKImage.FromBitmap(bitmap);

// Save to file
Console.WriteLine("Saving output...");
using var data = image.Encode(SKEncodedImageFormat.Png, 100);
using var stream = File.OpenWrite(OutputPath);
data.SaveTo(stream);

Console.WriteLine($"Output saved to: {Path.GetFullPath(OutputPath)}");
Console.WriteLine();

// Cleanup
Console.WriteLine("Cleaning up...");
cubeMesh.Dispose();
renderer.Dispose();
vulkanContext.Dispose();
vulkanInstance.Dispose();

Console.WriteLine("Done!");
Console.WriteLine();
Console.WriteLine("=== Test Complete ===");

return 0;
