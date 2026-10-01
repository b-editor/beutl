using System.Numerics;
using Beutl.Composition;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Textures;
using Beutl.Media;
using Beutl.Media.Source;
using SkiaSharp;

namespace Beutl.Graphics3DTests;

/// <summary>
/// A PBR texture map holding one uniform value must shade like the equivalent scalar factor. Colour maps are
/// sRGB-encoded and decode to linear; data maps (normal, metallic-roughness, AO) are raw values that must reach the
/// shader unchanged, so decoding them as sRGB would tilt the normals and darken roughness, metallic and AO.
/// </summary>
[TestFixture]
[NonParallelizable]
public class MaterialTextureColorSpaceTests
{
    private const int Size = 128;
    private const byte Mid = 128;
    private const float MidValue = Mid / 255f;

    private IGraphicsContext _context = null!;
    private string _directory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _context = GpuTestEnvironment.EnsureAvailable();
        _directory = Path.Combine(Path.GetTempPath(), "beutl-texture-colorspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        if (_directory != null && Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Test]
    public void AlbedoMap_DecodesSrgbLikeTheAlbedoColor()
    {
        byte[] factor = Render(m => m.Albedo.CurrentValue = new Color(255, Mid, Mid, Mid));
        byte[] mapped = Render(m => m.AlbedoMap.CurrentValue = SolidTexture("albedo", Mid, Mid, Mid));

        AssertSameShading(mapped, factor);
    }

    [Test]
    public void FlatNormalMap_KeepsTheGeometricNormal()
    {
        byte[] factor = Render(_ => { });
        byte[] mapped = Render(m => m.NormalMap.CurrentValue = SolidTexture("normal", Mid, Mid, 255));

        AssertSameShading(mapped, factor);
    }

    [Test]
    public void MetallicRoughnessMap_IsSampledAsRawData()
    {
        byte[] factor = Render(m =>
        {
            m.Metallic.CurrentValue = MidValue;
            m.Roughness.CurrentValue = MidValue;
        });
        byte[] mapped = Render(m =>
        {
            m.Metallic.CurrentValue = 1;
            m.Roughness.CurrentValue = 1;
            m.MetallicRoughnessMap.CurrentValue = SolidTexture("metallic-roughness", 0, Mid, Mid);
        });

        AssertSameShading(mapped, factor);
    }

    [Test]
    public void AOMap_IsSampledAsRawData()
    {
        byte[] factor = Render(m => m.AmbientOcclusion.CurrentValue = MidValue);
        byte[] mapped = Render(m => m.AOMap.CurrentValue = SolidTexture("ao", Mid, Mid, Mid));

        AssertSameShading(mapped, factor);
    }

    private ImageTextureSource SolidTexture(string name, byte r, byte g, byte b)
    {
        // An untagged PNG decodes as sRGB, which is how glTF and most DCC tools ship every map.
        string path = Path.Combine(_directory, name + ".png");
        using (var bitmap = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul)))
        {
            bitmap.Erase(new SKColor(r, g, b, 255));
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using FileStream stream = File.Create(path);
            data.SaveTo(stream);
        }

        var image = new ImageSource();
        image.ReadFrom(new Uri(path));
        var texture = new ImageTextureSource();
        texture.Source.CurrentValue = image;
        return texture;
    }

    private byte[] Render(Action<PBRMaterial> configure)
    {
        return GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            using var renderer = new Renderer3D(_context);
            renderer.Initialize(Size, Size);
            var composition = new CompositionContext(TimeSpan.Zero);

            var camera = new PerspectiveCamera();
            camera.Position.CurrentValue = new Vector3(0, 0, 3);
            camera.Target.CurrentValue = Vector3.Zero;
            using var cameraResource = (PerspectiveCamera.Resource)camera.ToResource(composition);

            var key = new DirectionalLight3D();
            key.Direction.CurrentValue = Vector3.Normalize(new Vector3(-1, -1, -1));
            key.Intensity.CurrentValue = 1f;
            var fill = new DirectionalLight3D();
            fill.Direction.CurrentValue = Vector3.Normalize(new Vector3(0.8f, -0.3f, -0.5f));
            fill.Intensity.CurrentValue = 0.3f;
            var lights = new List<Light3D.Resource>
            {
                (Light3D.Resource)key.ToResource(composition),
                (Light3D.Resource)fill.ToResource(composition),
            };

            var material = new PBRMaterial();
            material.Metallic.CurrentValue = 0.2f;
            material.Roughness.CurrentValue = 0.4f;
            configure(material);

            var sphere = new Sphere3D();
            sphere.Radius.CurrentValue = 1f;
            sphere.Segments.CurrentValue = 48;
            sphere.Rings.CurrentValue = 24;
            sphere.Material.CurrentValue = material;
            var objects = new List<Object3D.Resource> { (Object3D.Resource)sphere.ToResource(composition) };

            try
            {
                renderer.Render(composition, cameraResource, objects, lights, Colors.Black, Colors.White, 0.6f);
                return renderer.DownloadPixels();
            }
            finally
            {
                foreach (Object3D.Resource obj in objects)
                    obj.Dispose();
                foreach (Light3D.Resource light in lights)
                    light.Dispose();
            }
        });
    }

    private static void AssertSameShading(byte[] mapped, byte[] factor)
    {
        Assert.That(mapped, Has.Length.EqualTo(factor.Length));

        var a = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(mapped);
        var b = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(factor);
        float maxDelta = 0;
        float maxValue = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (i % 4 == 3)
                continue;

            // Highlights exceed 1, so the tolerance grows with the value.
            float expected = (float)b[i];
            maxDelta = MathF.Max(maxDelta, MathF.Abs((float)a[i] - expected) / MathF.Max(1, expected));
            maxValue = MathF.Max(maxValue, (float)b[i]);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(maxValue, Is.GreaterThan(0.1f), "The sphere must be lit for the comparison to mean anything.");
            // 8-bit maps quantize to 1/255, and a 128 normal map tilts the normal by under half a degree.
            Assert.That(maxDelta, Is.LessThan(0.05f),
                "A uniform texture map must shade like the scalar factor it encodes.");
        }
    }
}
