using System.Numerics;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Nodes;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class Scene3DMultisampleTests
{
    private const int Size = 128; // The legacy linear flip then samples exact texel centers.

    [Test]
    public void DefaultScene_ResolvesOnlyTheSilhouetteAndKeepsTheNearestDepth()
    {
        GpuTestEnvironment.EnsureAvailable();
        (Frame multisampled, Frame singleSample) = GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            Scene3D scene = CreateCubeScene();
            CompositionContext context = CompositionContext.Default;
            using Scene3D.Resource resource = scene.ToResource(context);
            using var node = new Scene3DRenderNode(resource);
            using var renderer = new RenderNodeRenderer(node, new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = new Rect(0, 0, Size, Size),
                OutputScale = 1,
                MaxWorkingScale = 1,
                CacheOptions = RenderCacheOptions.Disabled,
            });

            Assert.That(scene.AntiAliasing.Value, Is.EqualTo(AntiAliasingMode.Msaa4x));
            Frame multisampled = Capture(renderer, resource);
            Renderer3D? sceneRenderer = resource.Renderer;

            scene.AntiAliasing.Value = AntiAliasingMode.Off;
            bool versionBumped = false;
            resource.Reconcile(scene, context, ref versionBumped);
            Assert.That(node.Update(resource), Is.True);
            Frame singleSample = Capture(renderer, resource);
            Assert.That(resource.Renderer, Is.SameAs(sceneRenderer));
            return (multisampled, singleSample);
        });

        ReadOnlySpan<Half> off = MemoryMarshal.Cast<byte, Half>(singleSample.Color);
        ReadOnlySpan<Half> msaa = MemoryMarshal.Cast<byte, Half>(multisampled.Color);
        var offAlphas = new HashSet<float>();
        var partialAlphas = new HashSet<float>();
        for (int i = 3; i < off.Length; i += 4)
        {
            offAlphas.Add((float)off[i]);
            float alpha = (float)msaa[i];
            if (alpha is > 0f and < 1f)
                partialAlphas.Add(alpha);
        }

        Assert.That(offAlphas, Is.EquivalentTo(new[] { 0f, 1f }));
        Assert.That(partialAlphas, Has.Count.GreaterThan(2), "4x must produce at least three distinct partial alpha values.");
        Assert.That(multisampled.Samples, Is.EqualTo(4));
        Assert.That(singleSample.Samples, Is.EqualTo(1));

        // The cube is rolled about the view axis, so its visible face has constant depth.
        float faceDepth = singleSample.Depth[(Size / 2) * Size + Size / 2];
        Assert.That(faceDepth, Is.LessThan(1f));
        int newlyCovered = 0;
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int pixel = y * Size + x;
                if (!HasCoverageChange(off, x, y))
                {
                    if (!multisampled.Color.AsSpan(pixel * 8, 8).SequenceEqual(singleSample.Color.AsSpan(pixel * 8, 8)))
                        Assert.Fail($"Color changed away from the silhouette at ({x}, {y}). Off: "
                            + string.Join(", ", off.Slice(pixel * 4, 4).ToArray()) + ". MSAA: "
                            + string.Join(", ", msaa.Slice(pixel * 4, 4).ToArray()));
                    if (multisampled.Depth[pixel] != singleSample.Depth[pixel])
                        Assert.Fail($"Depth changed away from the silhouette at ({x}, {y}).");
                }

                float alpha = (float)msaa[pixel * 4 + 3];
                if (alpha is > 0f and < 1f)
                {
                    Assert.That(multisampled.Depth[pixel], Is.EqualTo(faceDepth), $"minimum depth at ({x}, {y})");
                    if (off[pixel * 4 + 3] == (Half)0)
                        newlyCovered++;
                }
            }
        }

        Assert.That(newlyCovered, Is.Positive, "Depth must pick up samples in pixels missed by the 1x center sample.");
    }

    [Test]
    public void Requested8x_UsesTheDeviceAnswerWithoutAnAllocationWarning()
    {
        IGraphicsContext context = GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() =>
        {
            int supported = context.GetSupportedSampleCount(8,
                [.. GeometryPass.GBufferFormats, TextureFormat.RGBA16Float, TextureFormat.Depth32Float]);
            using IRenderer3D renderer = IRenderer3D.Create(context, surfaceDensity: 1, sampleCount: 8);
            renderer.Initialize(32, 24);
            Assert.That(renderer.SampleCount, Is.EqualTo(supported));
            Assert.That(((Renderer3D)renderer).LastSampleAllocationFailure, Is.Null);
        });
    }

    private static Scene3D CreateCubeScene()
    {
        var scene = new Scene3D
        {
            RenderWidth = { Value = Size },
            RenderHeight = { Value = Size },
            // Keep the face color independent of the driver's fragment interpolation position.
            AmbientIntensity = { Value = 0.5f },
            Camera =
            {
                Value = new PerspectiveCamera
                {
                    Position = { Value = new Vector3(0, 0, 5) },
                    Target = { Value = Vector3.Zero },
                    Up = { Value = Vector3.UnitY },
                    FieldOfView = { Value = 45 },
                }
            }
        };
        scene.Objects.Add(new Cube3D
        {
            Width = { Value = 2 },
            Height = { Value = 2 },
            Depth = { Value = 2 },
            Rotation = { Value = new Vector3(0, 0, 30) },
            Material = { Value = new PBRMaterial { Albedo = { Value = new Color(255, 110, 173, 223) } } },
        });
        return scene;
    }

    private static Frame Capture(RenderNodeRenderer renderer, Scene3D.Resource resource)
    {
        using var raster = renderer.Rasterize();
        Assert.That(resource.Renderer, Is.Not.Null);
        var depth = new float[Size * Size];
        Assert.That(resource.TryCopyDepth(depth, out PixelSize size), Is.True);
        Assert.That(size, Is.EqualTo(new PixelSize(Size, Size)));
        return new Frame(resource.Renderer!.DownloadPixels(), depth, resource.Renderer.SampleCount);
    }

    private static bool HasCoverageChange(ReadOnlySpan<Half> pixels, int x, int y)
    {
        Half alpha = pixels[(y * Size + x) * 4 + 3];
        for (int row = Math.Max(0, y - 1); row <= Math.Min(Size - 1, y + 1); row++)
        {
            for (int column = Math.Max(0, x - 1); column <= Math.Min(Size - 1, x + 1); column++)
            {
                if (pixels[(row * Size + column) * 4 + 3] != alpha)
                    return true;
            }
        }

        return false;
    }

    private sealed record Frame(byte[] Color, float[] Depth, int Samples);
}
