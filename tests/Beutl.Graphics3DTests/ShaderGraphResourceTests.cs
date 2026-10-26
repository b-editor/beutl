using System.Numerics;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering.Requests;
using Beutl.Graphics.Shaders;
using static Beutl.Graphics3DTests.ShaderGraphRenderingTests;

namespace Beutl.Graphics3DTests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphResourceTests
{
    internal static ShaderImage Sample(ShaderGraphBuilder graph, ShaderImage image, string sampler = "sampler2D",
        string coordinates = "uv", string expression = "texture(image, COORD)", SamplerState? state = null)
    {
        ShaderImage output = graph.Image2D("sampled", graph.Source);
        var program = ShaderProgram.Fullscreen(new ShaderSource($$"""
            #version 450
            layout(location = 0) in vec2 uv;
            layout(location = 0) out vec4 color;
            layout(binding = 0) uniform {{sampler}} image;
            void main() { color = {{expression.Replace("COORD", coordinates)}}; }
            """));
        graph.Fullscreen("sample", program, pass =>
        {
            pass.SampledImage(0, image, state ?? SamplerState.NearestClamp);
            pass.ColorAttachment(0, output);
        });
        return output;
    }

    internal static void OnDevice(Action<ShaderCapabilities> action)
    {
        GpuTestEnvironment.EnsureAvailable();
        GpuTestEnvironment.InvokeOnRenderThread(() => action(ShaderCapabilities.Query(GraphicsContextFactory.SharedContext!)));
    }

    internal static void AssertRed(ShaderGraphDescription graph, float expected)
    {
        float[] pixels = Render(graph, SourceBounds);
        Assert.That(pixels.Where((_, i) => i % 4 == 0), Has.All.EqualTo(expected).Within(0.003));
        Assert.That(pixels.Where((_, i) => i % 4 == 3), Has.All.EqualTo(1).Within(0.003));
    }

    [TestCase(ImageKind.Image2D, "2D", "uv", 1, 0.25f)]
    [TestCase(ImageKind.Image2DArray, "2DArray", "vec3(uv, 1)", 2, 0.375f)]
    [TestCase(ImageKind.Image3D, "3D", "vec3(uv, 0.75)", 2, 0.375f)]
    [TestCase(ImageKind.Cube, "Cube", "vec3(1,0,0)", 6, 0.25f)]
    [TestCase(ImageKind.CubeArray, "CubeArray", "vec4(1,0,0,1)", 12, 1f)]
    public void ComputeWritesArraysVolumesAndCubeFaces(ImageKind kind, string suffix, string coordinates, int slices, float expected)
    {
        OnDevice(capabilities =>
        {
            if (kind == ImageKind.CubeArray && !capabilities.CubeArrays)
                Assert.Ignore("This device does not enable cube arrays.");
            var description = new ImageDescription(kind, 4, 4, Depth: kind == ImageKind.Image3D ? slices : 1,
                ArrayLayers: kind is ImageKind.Image3D or ImageKind.Image2D ? 1 : slices);
            Assert.That(capabilities.SupportsImage(description, ImageUsage.Storage | ImageUsage.Sampled), Is.True);
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage image = builder.Image("image", description);
                var writer = ShaderProgram.Compute(new ShaderSource($$"""
                    #version 450
                    layout(local_size_x = 1) in;
                    layout(binding = 0, rgba16f) writeonly uniform image{{suffix}} image;
                    void main() {
                        imageStore(image, {{(kind == ImageKind.Image2D ? "ivec2(gl_GlobalInvocationID.xy)" : "ivec3(gl_GlobalInvocationID)")}},
                            vec4(0.25 + 0.125 * float(gl_GlobalInvocationID.z), 0.5, 0.125, 1));
                    }
                    """));
                builder.Compute("write", writer, pass =>
                {
                    pass.StorageImage(0, image, ResourceAccess.Write);
                    pass.Dispatch(4, 4, slices);
                });
                return builder.Output(Sample(builder, image, "sampler" + suffix, coordinates));
            });
            AssertRed(graph, expected);
        });
    }

    [TestCase(TextureFormat.R32SInt, "r32i", "i", "ivec4(3)")]
    [TestCase(TextureFormat.R32UInt, "r32ui", "u", "uvec4(3)")]
    [TestCase(TextureFormat.RGBA32SInt, "rgba32i", "i", "ivec4(3)")]
    [TestCase(TextureFormat.RGBA32UInt, "rgba32ui", "u", "uvec4(3)")]
    [TestCase(TextureFormat.RG32Float, "rg32f", "", "vec4(3)")]
    public void NumericStorageFormatsCanBeConvertedToTheOutput(TextureFormat format, string qualifier, string prefix, string value)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage image = builder.Image("numeric", new(ImageKind.Image2D, 4, 4, format));
                builder.Compute("write", ShaderProgram.Compute(new ShaderSource($$"""
                    #version 450
                    layout(local_size_x = 1) in;
                    layout(binding = 0, {{qualifier}}) writeonly uniform {{prefix}}image2D image;
                    void main() { imageStore(image, ivec2(gl_GlobalInvocationID.xy), {{value}}); }
                    """)), pass => { pass.StorageImage(0, image, ResourceAccess.Write); pass.Dispatch(4, 4); });
                return builder.Output(Sample(builder, image, prefix + "sampler2D",
                    expression: "vec4(float(texture(image, COORD).r) / 4.0, 0, 0, 1)"));
            });
            AssertRed(graph, 0.75f);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IntegerAttachmentsAndTypedClearsPreserveIntegerBits(bool signed)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage image = builder.Image("integer", new(ImageKind.Image2D, 4, 4,
                    signed ? TextureFormat.RGBA32SInt : TextureFormat.RGBA32UInt));
                builder.Clear("clear", image, signed ? ImageClearValue.Int(-2) : ImageClearValue.UInt(2));
                ShaderImage copy = builder.Image("copy", new(ImageKind.Image2D, 4, 4, image.Format));
                builder.Copy("copy", image, copy);
                var program = ShaderProgram.Fullscreen(new ShaderSource($$"""
                    #version 450
                    layout(location = 0) out {{(signed ? "ivec4" : "uvec4")}} result;
                    void main() { if (gl_FragCoord.x < 2) discard; result = {{(signed ? "ivec4(-2)" : "uvec4(2)")}}; }
                    """));
                builder.Fullscreen("integer-attachment", program, pass => pass.ColorAttachment(0, copy, AttachmentLoadOp.Load));
                return builder.Output(Sample(builder, copy, signed ? "isampler2D" : "usampler2D",
                    expression: "vec4(abs(float(texture(image, COORD).r)) / 4.0, 0, 0, 1)"));
            });
            AssertRed(graph, 0.5f);
        });
    }

    [TestCase(4, SamplerFilter.Linear)]
    [TestCase(7, SamplerFilter.Linear)]
    [TestCase(4, SamplerFilter.Nearest)]
    public void MipsAndLayerViewsShareStorageAndTrackTheirOwnLayouts(int width, SamplerFilter filter)
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage image = builder.Image("array", new(ImageKind.Image2DArray, width, 4, ArrayLayers: 2, MipLevels: 3));
                ShaderImage chain = builder.View("chain", image, ImageKind.Image2D, mipLevels: 3, baseLayer: 1);
                ShaderImage first = builder.View("first", chain, ImageKind.Image2D);
                builder.Clear("clear-base", first, ImageClearValue.Float(new(0.5f, 0.25f, 0.125f, 1)));
                builder.GenerateMipmaps("mips", chain, filter);
                ShaderImage last = builder.View("last", chain, ImageKind.Image2D, baseMip: 2);
                ShaderImage other = builder.View("other", image, ImageKind.Image2D, baseMip: 2);
                builder.Copy("copy-layer", last, other);
                // Clearing a disjoint subresource must not change the layout or contents of 'other'.
                builder.Clear("clear-last", last, ImageClearValue.Float(Vector4.One));
                return builder.Output(Sample(builder, other));
            });
            AssertRed(graph, 0.5f);
        });
    }

    [Test]
    public void DisjointSampledAndAttachedLayersAreAllowed()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage array = builder.Image("array", new(ImageKind.Image2DArray, 4, 4, ArrayLayers: 2));
                ShaderImage first = builder.View("first", array, ImageKind.Image2D);
                ShaderImage second = builder.View("second", array, ImageKind.Image2D, baseLayer: 1);
                builder.Clear("clear", first, ImageClearValue.Float(new(0.25f, 0, 0, 1)));
                builder.Fullscreen("copy-pass", ShaderProgram.Fullscreen(new ShaderSource(Graphics3DTestHelpers.PassthroughFragmentShader)), pass =>
                {
                    pass.SampledImage(0, first);
                    pass.ColorAttachment(0, second);
                });
                return builder.Output(Sample(builder, second));
            });
            AssertRed(graph, 0.25f);
        });
    }

    [Test]
    public void UnwrittenLayersAndMipsRemainUninitialized()
    {
        foreach (bool mip in new[] { false, true })
            Assert.That(() => ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage array = builder.Image("array", new(ImageKind.Image2DArray, 4, 4, ArrayLayers: 2, MipLevels: 2));
                ShaderImage written = builder.View("written", array, ImageKind.Image2D);
                ShaderImage missing = builder.View("missing", array, ImageKind.Image2D, baseMip: mip ? 1 : 0, baseLayer: mip ? 0 : 1);
                builder.Clear("clear", written);
                return builder.Output(missing);
            }), Throws.InvalidOperationException.With.Message.Contains("uninitialized"));
    }

    [Test]
    public void InvalidTransfersArePreflightedAndDoNotPoisonTheNextGraph()
    {
        OnDevice(capabilities =>
        {
            Assert.That(capabilities.SupportsImage(new(ImageKind.Image2D, int.MaxValue, 1), ImageUsage.Sampled), Is.False);
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage source = builder.Image("small", new(ImageKind.Image2D, 4, 4));
                ShaderImage destination = builder.Image("large", new(ImageKind.Image2D, 8, 4));
                builder.Clear("clear", source);
                builder.Copy("invalid-copy", source, destination);
                return builder.Output(Sample(builder, destination));
            });
            Assert.That(() => Render(graph, SourceBounds), Throws.Exception.With.Message.Contains("equal texel extents"));
            AssertRed(ShaderGraphTests.ColorGraph(new(0.25f, 0, 0, 1)), 0.25f);
        });
    }

    [Test]
    public void SamplerLodSelectsMipLevelsAndInvalidatesOnlyItsPipeline()
    {
        OnDevice(_ =>
        {
            using var cache = ShaderGraphPipeline.CreateCache();
            using var pool = new RenderTargetPool(factory: null);
            long creations = 0;
            foreach (int lod in new[] { 0, 1 })
            {
                ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
                {
                    ShaderImage image = builder.Image("mips", new(ImageKind.Image2D, 4, 4, MipLevels: 2));
                    builder.Clear("initialize", image, ImageClearValue.Float(new(0.25f, 0, 0, 1)));
                    ShaderImage second = builder.View("second", image, ImageKind.Image2D, baseMip: 1);
                    builder.Clear("second", second, ImageClearValue.Float(new(0.75f, 0, 0, 1)));
                    return builder.Output(Sample(builder, image, expression: "textureLod(image, COORD, 0)",
                        state: SamplerState.NearestClamp with { MinLod = lod, MaxLod = lod }));
                });
                float[] pixels = Render(graph, SourceBounds, cache: cache, sharedPool: pool);
                Assert.That(pixels[0], Is.EqualTo(lod == 0 ? 0.25f : 0.75f));
                if (lod == 1) Assert.That(cache.Statistics.Creations, Is.EqualTo(creations + 1));
                creations = cache.Statistics.Creations;
            }
        });
    }

    [Test]
    public void LinearMipFilteringOfIntegerImagesIsRejectedEvenForOneMip()
    {
        OnDevice(_ =>
        {
            ShaderGraphDescription graph = ShaderGraphBuilder.Build(0, (builder, _) =>
            {
                ShaderImage image = builder.Image("integer", new(ImageKind.Image2D, 4, 4, TextureFormat.R32UInt));
                builder.Clear("clear", image, ImageClearValue.UInt(1));
                return builder.Output(Sample(builder, image, "usampler2D", expression: "vec4(texture(image, COORD))",
                    state: SamplerState.NearestClamp with { MipmapFilter = SamplerFilter.Linear }));
            });
            Assert.That(() => Render(graph, SourceBounds), Throws.Exception.With.Message.Contains("SampledImageFilterLinearBit"));
        });
    }

    [Test]
    public void ClearValuesChangeOutputButNotStructureAndViewsKeepTheirParentAlive()
    {
        ShaderGraphDescription Record(float value) => ShaderGraphBuilder.Build(0, (builder, _) =>
        {
            ShaderImage parent = builder.Image("parent", new(ImageKind.Image2DArray, 4, 4, ArrayLayers: 2));
            ShaderImage child = builder.View("child", parent, ImageKind.Image2D, baseLayer: 1);
            builder.Clear("clear", child, ImageClearValue.Float(new(value, 0, 0, 1)));
            return builder.Output(Sample(builder, child));
        });
        ShaderGraphDescription first = Record(0.25f), second = Record(0.75f);
        Assert.That(first.StructuralIdentity, Is.EqualTo(second.StructuralIdentity));
        Assert.That(first.OutputIdentity, Is.Not.EqualTo(second.OutputIdentity));
        Assert.That(first.LastUses[1], Is.EqualTo(first.LastUses[2]));
        OnDevice(_ => { AssertRed(first, 0.25f); AssertRed(second, 0.75f); });
    }
}
