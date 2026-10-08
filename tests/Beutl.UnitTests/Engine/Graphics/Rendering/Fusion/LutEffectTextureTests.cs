using System.Numerics;
using System.Text;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.UnitTests.Engine.Graphics.Backend;
using SkiaSharp;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Fusion;

[TestFixture]
[NonParallelizable]
public sealed class LutEffectTextureTests
{
    private static readonly Color[] s_colors =
    [
        new(255, 255, 0, 0),
        new(255, 0, 255, 0),
        new(255, 0, 0, 255),
        new(255, 255, 255, 255),
        new(255, 0, 0, 0),
        new(255, 64, 128, 192),
        new(128, 255, 0, 0),
        new(0, 128, 64, 192),
    ];

    private static readonly (CubeFileDimension Dimension, int Size)[] s_tables =
    [
        (CubeFileDimension.OneDimension, 17),
        (CubeFileDimension.OneDimension, 65536),
        (CubeFileDimension.ThreeDimension, 2),
        (CubeFileDimension.ThreeDimension, 33),
        (CubeFileDimension.ThreeDimension, 65),
    ];

    private string _directory = null!;

    [OneTimeSetUp]
    public void CreateTables()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"beutl-lut-textures-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        foreach ((CubeFileDimension dimension, int size) in s_tables)
        {
            int length = dimension == CubeFileDimension.OneDimension ? size : size * size * size;
            using var writer = new StreamWriter(TablePath(dimension, size), false, Encoding.ASCII);
            writer.WriteLine($"TITLE \"invert-{dimension}-{size}\"");
            writer.WriteLine($"LUT_{(dimension == CubeFileDimension.OneDimension ? "1D" : "3D")}_SIZE {size}");
            for (int index = 0; index < length; index++)
            {
                Vector3 value = dimension == CubeFileDimension.OneDimension
                    ? new Vector3(index / (float)(size - 1))
                    : new Vector3(
                        index % size / (float)(size - 1),
                        index / size % size / (float)(size - 1),
                        index / (size * size) / (float)(size - 1));
                value = Vector3.One - value;
                writer.WriteLine(FormattableString.Invariant($"{value.X} {value.Y} {value.Z}"));
            }
        }
    }

    [OneTimeTearDown]
    public void RemoveTables()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [TestCase(CubeFileDimension.OneDimension, 17)]
    [TestCase(CubeFileDimension.OneDimension, 65536)]
    [TestCase(CubeFileDimension.ThreeDimension, 2)]
    [TestCase(CubeFileDimension.ThreeDimension, 33)]
    [TestCase(CubeFileDimension.ThreeDimension, 65)]
    public void CpuLookup_PreservesColorsAndAlpha(CubeFileDimension dimension, int size)
    {
        SKColor[] pixels = Render(CreateSource(dimension, size), RenderIntent.Preview, new CpuTargetFactory());

        AssertInvertedEndpointsAndAlpha(pixels);
    }

    [TestCase(CubeFileDimension.OneDimension, 65536, RenderIntent.Preview)]
    [TestCase(CubeFileDimension.OneDimension, 65536, RenderIntent.Delivery)]
    [TestCase(CubeFileDimension.ThreeDimension, 33, RenderIntent.Preview)]
    [TestCase(CubeFileDimension.ThreeDimension, 33, RenderIntent.Delivery)]
    [TestCase(CubeFileDimension.ThreeDimension, 65, RenderIntent.Preview)]
    [TestCase(CubeFileDimension.ThreeDimension, 65, RenderIntent.Delivery)]
    public void LargeLut_GpuLookupMatchesCpu(CubeFileDimension dimension, int size, RenderIntent intent)
    {
        VulkanTestEnvironment.EnsureAvailable();
        CubeSource source = CreateSource(dimension, size);
        VulkanTestEnvironment.InvokeOnRenderThread(() =>
        {
            SKColor[] expected = Render(source, intent, new CpuTargetFactory());
            SKColor[] actual = Render(source, intent);

            AssertInvertedEndpointsAndAlpha(actual);
            Assert.Multiple(() =>
            {
                for (int index = 0; index < actual.Length; index++)
                {
                    string message = $"{dimension}, size={size}, intent={intent}, sample={index}";
                    Assert.That(actual[index].Red, Is.EqualTo(expected[index].Red).Within(2), message);
                    Assert.That(actual[index].Green, Is.EqualTo(expected[index].Green).Within(2), message);
                    Assert.That(actual[index].Blue, Is.EqualTo(expected[index].Blue).Within(2), message);
                    Assert.That(actual[index].Alpha, Is.EqualTo(expected[index].Alpha).Within(1), message);
                }
            });
        });
    }

    private string TablePath(CubeFileDimension dimension, int size)
        => Path.Combine(_directory, $"{dimension}-{size}.cube");

    private CubeSource CreateSource(CubeFileDimension dimension, int size)
    {
        var source = new CubeSource();
        source.ReadFrom(new Uri(TablePath(dimension, size)));
        return source;
    }

    private static void AssertInvertedEndpointsAndAlpha(SKColor[] pixels)
    {
        Assert.Multiple(() =>
        {
            for (int index = 0; index < 5; index++)
            {
                Assert.That(pixels[index].Red, Is.EqualTo(255 - s_colors[index].R).Within(2), $"sample={index}, red");
                Assert.That(pixels[index].Green, Is.EqualTo(255 - s_colors[index].G).Within(2), $"sample={index}, green");
                Assert.That(pixels[index].Blue, Is.EqualTo(255 - s_colors[index].B).Within(2), $"sample={index}, blue");
            }

            for (int index = 0; index < pixels.Length; index++)
                Assert.That(pixels[index].Alpha, Is.EqualTo(s_colors[index].A).Within(1), $"sample={index}, alpha");
        });
    }

    private static SKColor[] Render(CubeSource source, RenderIntent intent, IRenderTargetFactory? factory = null)
    {
        var effect = new LutEffect { Source = { CurrentValue = source } };
        Brush.Resource[] fills = s_colors
            .Select(color => new SolidColorBrush(color).ToResource(CompositionContext.Default)).ToArray();
        try
        {
            using var root = new FilterEffectRenderNode(effect.ToResource(CompositionContext.Default));
            for (int index = 0; index < fills.Length; index++)
                root.AddChild(new RectangleRenderNode(new Rect(index * 4, 0, 4, 4), fills[index], null));

            using var renderer = new RenderNodeRenderer(root, new RenderNodeRenderRequest
            {
                Intent = intent,
                TargetDomain = new Rect(0, 0, fills.Length * 4, 4),
                CacheOptions = RenderCacheOptions.Disabled,
            }, factory);
            using RenderNodeRasterization rasterization = renderer.Rasterize();
            Bitmap bitmap = rasterization.Bitmap!;
            Assert.That(bitmap, Is.Not.Null);
            return Enumerable.Range(0, fills.Length)
                .Select(index => bitmap.SKBitmap.GetPixel(index * 4 + 2, 2)).ToArray();
        }
        finally
        {
            foreach (Brush.Resource fill in fills)
                fill.Dispose();
        }
    }

    private sealed class CpuTargetFactory : IRenderTargetFactory
    {
        public RenderTarget Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize size = allocation.DeviceSize;
            SKSurface surface = SKSurface.Create(new SKImageInfo(
                size.Width, size.Height, SKColorType.RgbaF16, SKAlphaType.Premul,
                allocation.PixelFormat.GetColorSpace().SKColorSpace))
                ?? throw new InvalidOperationException("Could not create the CPU LUT test surface.");
            return new CpuRenderTarget(surface, size);
        }
    }

    private sealed class CpuRenderTarget(SKSurface surface, PixelSize size)
        : RenderTarget(surface, size.Width, size.Height);
}
