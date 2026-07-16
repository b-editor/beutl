using System.Numerics;
using System.Reflection;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Meshes;
using Beutl.Graphics3D.Models;
using Beutl.Graphics3D.Nodes;
using Beutl.Graphics3D.Primitives;
using Beutl.Graphics3D.Textures;
using Moq;
using DrawableRenderNode = Beutl.Graphics.Rendering.DrawableRenderNode;
using RenderIntent = Beutl.Graphics.Rendering.RenderIntent;
using RenderPullPurpose = Beutl.Graphics.Rendering.RenderPullPurpose;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public class Renderer3DDisposalTests
{
    [Test]
    public void Initialize_PreservesAllocationFailureAndSweepsLaterPasses_WhenEarlyCleanupThrows()
    {
        var allocationFailure = new InvalidOperationException("allocation failure");
        var cleanupFailure = new InvalidOperationException("flip cleanup failure");
        var compiler = new Mock<IShaderCompiler>();
        compiler.Setup(x => x.CompileToSpirv(
                It.IsAny<string>(), It.IsAny<ShaderStage>(), It.IsAny<string>()))
            .Returns([]);
        compiler.As<IDisposable>();

        var textures = Enumerable.Range(0, 9)
            .Select(_ => new Mock<ITexture2D>())
            .ToArray();
        bool geometryTextureDisposed = false;
        textures[0].Setup(x => x.Dispose()).Callback(() => geometryTextureDisposed = true);
        int textureIndex = 0;

        var descriptors = Enumerable.Range(0, 3)
            .Select(_ => new Mock<IDescriptorSet>())
            .ToArray();
        descriptors[2].Setup(x => x.Dispose()).Throws(cleanupFailure);
        int descriptorIndex = 0;

        Mock<IGraphicsContext> context = CreateContext(compiler.Object);
        context.Setup(x => x.CreateTexture2D(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(() => textureIndex < textures.Length
                ? textures[textureIndex++].Object
                : throw allocationFailure);
        context.Setup(x => x.CreateDescriptorSet(
                It.IsAny<IPipeline3D>(), It.IsAny<DescriptorPoolSize[]>()))
            .Returns(() => descriptors[descriptorIndex++].Object);

        var renderer = new Renderer3D(context.Object);
        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => renderer.Initialize(16, 16));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(allocationFailure),
                    "cleanup must not replace the allocation failure");
                Assert.That(geometryTextureDisposed, Is.True,
                    "a throwing early pass cleanup must not strand later passes");
            });
        }
        finally
        {
            try
            {
                renderer.Dispose();
            }
            catch
            {
                // The assertions above own the failure contract under test.
            }
        }
    }

    [Test]
    public void Dispose_SweepsRemainingOwnedResourcesAndThrowsFirstFailure()
    {
        var cleanupFailure = new InvalidOperationException("first cleanup failure");
        bool flipOutputTextureDisposed = false;
        bool outputTextureDisposed = false;
        bool compilerDisposed = false;

        var compiler = new Mock<IShaderCompiler>();
        compiler.As<IDisposable>().Setup(x => x.Dispose()).Callback(() => compilerDisposed = true);
        var context = new Mock<IGraphicsContext>();
        context.Setup(x => x.CreateShaderCompiler()).Returns(compiler.Object);

        var throwingDescriptor = new Mock<IDescriptorSet>();
        throwingDescriptor.Setup(x => x.Dispose()).Throws(cleanupFailure);
        var flipPass = new FlipPass(context.Object, compiler.Object);
        SetField(flipPass, "_descriptorSet", throwingDescriptor.Object);
        var flipOutputTexture = new Mock<ITexture2D>();
        flipOutputTexture.Setup(x => x.Dispose()).Callback(() => flipOutputTextureDisposed = true);
        SetField(flipPass, "<OutputTexture>k__BackingField", flipOutputTexture.Object);

        var outputTexture = new Mock<ITexture2D>();
        outputTexture.Setup(x => x.Dispose()).Callback(() => outputTextureDisposed = true);

        var renderer = new Renderer3D(context.Object);
        SetField(renderer, "_flipPass", flipPass);
        SetField(renderer, "_outputTexture", outputTexture.Object);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(renderer.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(flipOutputTextureDisposed, Is.True,
                "an early pass resource failure must not strand later resources in the same pass");
            Assert.That(outputTextureDisposed, Is.True,
                "a throwing pass must not strand the output texture");
            Assert.That(compilerDisposed, Is.True,
                "a throwing pass must not strand the shader compiler");
        });
    }

    [Test]
    public void Resize_CommitsNewStateAndSweepsOldResources_WhenOldPassCleanupThrows()
    {
        var cleanupFailure = new InvalidOperationException("old lighting cleanup failure");
        bool oldOutputDisposed = false;

        var compiler = new Mock<IShaderCompiler>();
        compiler.Setup(x => x.CompileToSpirv(
                It.IsAny<string>(), It.IsAny<ShaderStage>(), It.IsAny<string>()))
            .Returns([]);
        compiler.As<IDisposable>();
        Mock<IGraphicsContext> context = CreateContext(compiler.Object);
        context.Setup(x => x.CreateTexture2D(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(() => new Mock<ITexture2D>().Object);
        context.Setup(x => x.CreateDescriptorSet(
                It.IsAny<IPipeline3D>(), It.IsAny<DescriptorPoolSize[]>()))
            .Returns(() => new Mock<IDescriptorSet>().Object);

        var geometryPass = new GeometryPass(context.Object, compiler.Object);
        geometryPass.Initialize(16, 16);
        var flipPass = new FlipPass(context.Object, compiler.Object);
        flipPass.Initialize(16, 16);

        var oldLightingPass = new LightingPass(
            context.Object, compiler.Object, geometryPass.DepthTexture!);
        var throwingDescriptor = new Mock<IDescriptorSet>();
        throwingDescriptor.Setup(x => x.Dispose()).Throws(cleanupFailure);
        SetField(oldLightingPass, "_descriptorSet", throwingDescriptor.Object);

        var oldOutput = new Mock<ITexture2D>();
        oldOutput.Setup(x => x.Dispose()).Callback(() => oldOutputDisposed = true);

        var renderer = new Renderer3D(context.Object);
        SetField(renderer, "_geometryPass", geometryPass);
        SetField(renderer, "_lightingPass", oldLightingPass);
        SetField(renderer, "_flipPass", flipPass);
        SetField(renderer, "_outputTexture", oldOutput.Object);
        SetField(renderer, "<Width>k__BackingField", 16);
        SetField(renderer, "<Height>k__BackingField", 16);
        SetField(renderer, "_initialized", true);

        try
        {
            InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
                () => renderer.Resize(32, 24));

            Assert.Multiple(() =>
            {
                Assert.That(actual, Is.SameAs(cleanupFailure));
                Assert.That(oldOutputDisposed, Is.True,
                    "the first old-pass failure must not strand later old resources");
                Assert.That(renderer.Width, Is.EqualTo(32));
                Assert.That(renderer.Height, Is.EqualTo(24));
                Assert.That(GetField<LightingPass>(renderer, "_lightingPass"),
                    Is.Not.SameAs(oldLightingPass),
                    "the complete new state must be committed before fallible old-state teardown");
            });
        }
        finally
        {
            renderer.Dispose();
        }
    }

    [Test]
    public void RenderNode3D_LifecycleRejectsInvalidTransitionsAndPreservesResizeContract()
    {
        var context = new Mock<IGraphicsContext>();
        var node = new LifecycleProbeRenderNode(context.Object);

        Assert.Throws<InvalidOperationException>(node.Execute);

        node.Initialize(16, 12);
        node.Resize(16, 12);
        node.Resize(24, 18);

        Assert.Multiple(() =>
        {
            Assert.That(node.IsInitialized, Is.True);
            Assert.That(node.InitializeCount, Is.EqualTo(1));
            Assert.That(node.ResizeCount, Is.EqualTo(1),
                "resizing to the current dimensions must remain a no-op");
            Assert.That(node.Width, Is.EqualTo(24));
            Assert.That(node.Height, Is.EqualTo(18));
            Assert.Throws<InvalidOperationException>(() => node.Initialize(32, 32));
        });

        node.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(node.IsDisposed, Is.True);
            Assert.That(node.IsInitialized, Is.False);
            Assert.That(node.DisposeCount, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => node.Initialize(32, 32));
            Assert.Throws<ObjectDisposedException>(() => node.Resize(32, 32));
            Assert.Throws<ObjectDisposedException>(node.Execute);
        });
    }

    [Test]
    public void FlipPass_DisposeMakesEveryNativeOperationTerminal()
    {
        var compiler = CreateShaderCompiler();
        Mock<IGraphicsContext> context = CreateContext(compiler.Object);
        var pass = new FlipPass(context.Object, compiler.Object);
        var input = new Mock<ITexture2D>();

        pass.Initialize(16, 12);
        pass.Resize(16, 12);
        int allocationCount = CountNativeAllocations(context);
        Assert.Throws<InvalidOperationException>(() => pass.Initialize(16, 12));
        Assert.That(CountNativeAllocations(context), Is.EqualTo(allocationCount));

        pass.Dispose();
        allocationCount = CountNativeAllocations(context);

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => pass.Initialize(16, 12));
            Assert.Throws<ObjectDisposedException>(() => pass.Resize(32, 24));
            Assert.Throws<ObjectDisposedException>(() => pass.SetInputTexture(input.Object));
            Assert.Throws<ObjectDisposedException>(pass.Execute);
            Assert.Throws<ObjectDisposedException>(pass.PrepareForSampling);
            Assert.That(CountNativeAllocations(context), Is.EqualTo(allocationCount),
                "terminal operations must not allocate replacement native resources");
        });
    }

    [Test]
    public void Renderer3D_DisposePreventsReinitializationAndResizeAllocations()
    {
        var compiler = CreateShaderCompiler();
        Mock<IGraphicsContext> context = CreateContext(compiler.Object);
        var renderer = new Renderer3D(context.Object);

        renderer.Initialize(16, 12);
        renderer.Resize(16, 12);
        int allocationCount = CountNativeAllocations(context);
        Assert.Throws<InvalidOperationException>(() => renderer.Initialize(16, 12));
        Assert.That(CountNativeAllocations(context), Is.EqualTo(allocationCount));

        renderer.Dispose();
        allocationCount = CountNativeAllocations(context);

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(() => renderer.Initialize(32, 24));
            Assert.Throws<ObjectDisposedException>(() => renderer.Resize(32, 24));
            Assert.Throws<ObjectDisposedException>(() => { _ = renderer.CreateSkiaSurface(); });
            Assert.Throws<ObjectDisposedException>(() => { _ = renderer.DownloadPixels(); });
            Assert.That(CountNativeAllocations(context), Is.EqualTo(allocationCount),
                "a disposed renderer must not recreate any pass or texture");
        });
    }

    [Test]
    public void ShadowManager_DisposePreventsLazyReinitialization()
    {
        var compiler = CreateShaderCompiler();
        Mock<IGraphicsContext> context = CreateContext(compiler.Object);
        var manager = new ShadowManager(context.Object, compiler.Object);

        manager.Initialize();
        int allocationCount = CountNativeAllocations(context);
        Assert.Throws<InvalidOperationException>(manager.Initialize);
        Assert.That(CountNativeAllocations(context), Is.EqualTo(allocationCount));

        manager.Dispose();
        allocationCount = CountNativeAllocations(context);

        Assert.Multiple(() =>
        {
            Assert.Throws<ObjectDisposedException>(manager.Initialize);
            Assert.Throws<ObjectDisposedException>(() => manager.RenderShadows([], [], Vector3.Zero, 1));
            Assert.Throws<ObjectDisposedException>(manager.PrepareForSampling);
            Assert.That(CountNativeAllocations(context), Is.EqualTo(allocationCount),
                "RenderShadows must not lazily recreate shadow passes after disposal");
        });
    }

    [Test]
    public void PointShadowPass_DisposeSweepsEveryFaceAndThrowsFirstFailure()
    {
        var cleanupFailure = new InvalidOperationException("first face cleanup failure");
        bool firstDummyTextureDisposed = false;
        bool lastDepthTextureDisposed = false;
        bool cubeTextureDisposed = false;

        var context = new Mock<IGraphicsContext>();
        var compiler = new Mock<IShaderCompiler>();
        var pass = new PointShadowPass(context.Object, compiler.Object);

        IFramebuffer3D?[] framebuffers =
            GetField<IFramebuffer3D?[]>(pass, "_faceFramebuffers")!;
        ITexture2D?[] dummyTextures =
            GetField<ITexture2D?[]>(pass, "_faceDummyTextures")!;
        ITexture2D?[] depthTextures =
            GetField<ITexture2D?[]>(pass, "_faceDepthTextures")!;

        var firstFramebuffer = new Mock<IFramebuffer3D>();
        firstFramebuffer.Setup(x => x.Dispose()).Throws(cleanupFailure);
        framebuffers[0] = firstFramebuffer.Object;

        var firstDummyTexture = new Mock<ITexture2D>();
        firstDummyTexture.Setup(x => x.Dispose()).Callback(() => firstDummyTextureDisposed = true);
        dummyTextures[0] = firstDummyTexture.Object;

        var lastDepthTexture = new Mock<ITexture2D>();
        lastDepthTexture.Setup(x => x.Dispose()).Callback(() => lastDepthTextureDisposed = true);
        depthTextures[^1] = lastDepthTexture.Object;

        var cubeTexture = new Mock<ITextureCube>();
        cubeTexture.Setup(x => x.Dispose()).Callback(() => cubeTextureDisposed = true);
        SetField(pass, "<ShadowCubeTexture>k__BackingField", cubeTexture.Object);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(pass.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(cleanupFailure));
            Assert.That(firstDummyTextureDisposed, Is.True,
                "a framebuffer failure must not skip the remaining resources of that face");
            Assert.That(lastDepthTextureDisposed, Is.True,
                "a failure in the first face must not skip later faces");
            Assert.That(cubeTextureDisposed, Is.True,
                "a face cleanup failure must not skip resources after the face sweep");
        });
    }

    [Test]
    public void MeshBufferUpload_IndexAllocationFailureReclaimsVertexAndPreservesPrimary()
    {
        var allocationFailure = new InvalidOperationException("index allocation failure");
        var cleanupFailure = new InvalidOperationException("vertex cleanup failure");
        bool vertexDisposed = false;
        int allocationIndex = 0;
        var vertexBuffer = new Mock<IBuffer>();
        vertexBuffer.Setup(x => x.Dispose()).Callback(() => vertexDisposed = true).Throws(cleanupFailure);
        var context = new Mock<IGraphicsContext>();
        context.Setup(x => x.CreateBuffer(
                It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns(() => allocationIndex++ == 0
                ? vertexBuffer.Object
                : throw allocationFailure);

        var mesh = new CubeMesh();
        using var resource = (CubeMesh.Resource)mesh.ToResource(CompositionContext.Default);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(
            () => MeshBufferUploadHelper.Ensure(context.Object, resource));

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(allocationFailure),
                "cleanup must not replace the index allocation failure");
            Assert.That(vertexDisposed, Is.True,
                "the device-local vertex buffer must be reclaimed when index allocation fails");
            Assert.That(resource.VertexBuffer, Is.Null);
            Assert.That(resource.IndexBuffer, Is.Null);
            Assert.That(resource.BuffersDirty, Is.True);
        });
    }

    [Test]
    public void GeneratedMaterialResource_DisposeSweepsSiblingAndPostDisposeAndPreservesFirstFailure()
    {
        var childFailure = new InvalidOperationException("texture cleanup failure");
        var pipelineFailure = new InvalidOperationException("pipeline cleanup failure");
        bool siblingDisposed = false;
        bool pipelineDisposed = false;

        var firstTexture = new TestTextureResource(() => throw childFailure);
        var siblingTexture = new TestTextureResource(() => siblingDisposed = true);
        var pipeline = new Mock<IPipeline3D>();
        pipeline.Setup(x => x.Dispose())
            .Callback(() => pipelineDisposed = true)
            .Throws(pipelineFailure);

        var material = new PBRMaterial.Resource
        {
            AlbedoMap = firstTexture,
            NormalMap = siblingTexture
        };
        SetField(material, "_pipeline", pipeline.Object);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(material.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(childFailure));
            Assert.That(siblingDisposed, Is.True,
                "a throwing generated child must not skip later generated children");
            Assert.That(pipelineDisposed, Is.True,
                "a throwing generated child must not skip the material PostDispose hook");
            Assert.That(material.IsDisposed, Is.True);
            Assert.That(firstTexture.IsDisposed, Is.True);
            Assert.That(siblingTexture.IsDisposed, Is.True);
        });
    }

    [Test]
    public void GeneratedMeshOwner_DisposeSweepsBaseMaterialAfterMeshFailure()
    {
        var meshFailure = new InvalidOperationException("vertex buffer cleanup failure");
        bool indexBufferDisposed = false;
        bool materialPipelineDisposed = false;

        var vertexBuffer = new Mock<IBuffer>();
        vertexBuffer.Setup(x => x.Dispose()).Throws(meshFailure);
        var indexBuffer = new Mock<IBuffer>();
        indexBuffer.Setup(x => x.Dispose()).Callback(() => indexBufferDisposed = true);
        var mesh = new CubeMesh.Resource
        {
            VertexBuffer = vertexBuffer.Object,
            IndexBuffer = indexBuffer.Object
        };

        var materialPipeline = new Mock<IPipeline3D>();
        materialPipeline.Setup(x => x.Dispose()).Callback(() => materialPipelineDisposed = true);
        var material = new BasicMaterial.Resource();
        SetField(material, "_pipeline", materialPipeline.Object);

        var owner = new MeshObject3D.Resource
        {
            Mesh = mesh,
            Material = material
        };

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(owner.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(meshFailure));
            Assert.That(indexBufferDisposed, Is.True,
                "the mesh PostDispose hook must sweep both buffers");
            Assert.That(materialPipelineDisposed, Is.True,
                "a derived generated-resource failure must not skip base generated-resource cleanup");
            Assert.That(owner.IsDisposed, Is.True);
            Assert.That(mesh.IsDisposed, Is.True);
            Assert.That(material.IsDisposed, Is.True);
        });
    }

    [Test]
    public void MaterialResource_FinalizerDoesNotDisposeManagedGpuOwners()
    {
        bool pipelineDisposed = false;
        var pipeline = new Mock<IPipeline3D>();
        pipeline.Setup(x => x.Dispose()).Callback(() => pipelineDisposed = true);
        var resource = new BasicMaterial.Resource();
        SetField(resource, "_pipeline", pipeline.Object);
        MethodInfo finalizer = typeof(EngineObject.Resource).GetMethod(
            "Finalize",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        try
        {
            Assert.DoesNotThrow(() => finalizer.Invoke(resource, null));
            Assert.Multiple(() =>
            {
                Assert.That(resource.IsDisposed, Is.True);
                Assert.That(pipelineDisposed, Is.False,
                    "managed GPU owners must only be disposed on the explicit disposing=true path");
            });
        }
        finally
        {
            GC.SuppressFinalize(resource);
            pipeline.Object.Dispose();
        }
    }

    [Test]
    public void PlaneResource_MeshFailureStillDetachesOwnedFieldAndSweepsMeshBuffers()
    {
        var failure = new InvalidOperationException("vertex buffer");
        bool indexBufferDisposed = false;
        var vertexBuffer = new Mock<IBuffer>();
        vertexBuffer.Setup(x => x.Dispose()).Throws(failure);
        var indexBuffer = new Mock<IBuffer>();
        indexBuffer.Setup(x => x.Dispose()).Callback(() => indexBufferDisposed = true);
        var mesh = new PlaneMesh.Resource
        {
            VertexBuffer = vertexBuffer.Object,
            IndexBuffer = indexBuffer.Object
        };
        var resource = new Plane3D.Resource();
        SetField(resource, "_meshResource", mesh);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(indexBufferDisposed, Is.True);
            Assert.That(GetField<PlaneMesh.Resource>(resource, "_meshResource"), Is.Null,
                "the primitive must detach its owned mesh resource before fallible cleanup");
            Assert.That(resource.IsDisposed, Is.True);
            Assert.That(mesh.IsDisposed, Is.True);
        });
    }

    [Test]
    public void DrawableTextureResource_FirstCacheFailureStillSweepsLaterCacheAndDetachesBoth()
    {
        var failure = new InvalidOperationException("frame cache");
        bool auxiliaryNodeDisposed = false;
        var drawable = new FallbackDrawable();
        using Drawable.Resource drawableResource = drawable.ToResource(CompositionContext.Default);
        var frameNode = new DisposalProbeDrawableRenderNode(drawableResource, () => throw failure);
        var auxiliaryNode = new DisposalProbeDrawableRenderNode(
            drawableResource,
            () => auxiliaryNodeDisposed = true);
        var resource = new DrawableTextureSource.Resource();
        object frameCache = GetField<object>(resource, "_frameCache")!;
        object auxiliaryCache = GetField<object>(resource, "_auxiliaryCache")!;
        SetProperty(frameCache, "DrawableNode", frameNode);
        SetProperty(auxiliaryCache, "DrawableNode", auxiliaryNode);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(auxiliaryNodeDisposed, Is.True,
                "a frame-cache failure must not strand the auxiliary cache");
            Assert.That(GetField<object>(resource, "_frameCache"), Is.Null);
            Assert.That(GetField<object>(resource, "_auxiliaryCache"), Is.Null);
        });
    }

    [Test]
    public void ImageTextureResource_TextureFailureStillDetachesGpuTexture()
    {
        var failure = new InvalidOperationException("gpu texture");
        var texture = new Mock<ITexture2D>();
        texture.Setup(x => x.Dispose()).Throws(failure);
        var resource = new ImageTextureSource.Resource();
        SetField(resource, "_gpuTexture", texture.Object);
        SetField(resource, "_gpuTextureVersion", 42);

        InvalidOperationException? actual = Assert.Throws<InvalidOperationException>(resource.Dispose);

        Assert.Multiple(() =>
        {
            Assert.That(actual, Is.SameAs(failure));
            Assert.That(GetField<ITexture2D>(resource, "_gpuTexture"), Is.Null);
            Assert.That(GetField<int>(resource, "_gpuTextureVersion"), Is.EqualTo(-1));
            Assert.That(resource.IsDisposed, Is.True);
        });
    }

    private static Mock<IGraphicsContext> CreateContext(IShaderCompiler compiler)
    {
        var context = new Mock<IGraphicsContext>();
        context.Setup(x => x.CreateShaderCompiler()).Returns(compiler);
        context.Setup(x => x.CreateTexture2D(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(() => new Mock<ITexture2D>().Object);
        context.Setup(x => x.CreateTextureCube(
                It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(() => new Mock<ITextureCube>().Object);
        context.Setup(x => x.CreateRenderPass3D(
                It.IsAny<IReadOnlyList<TextureFormat>>(),
                It.IsAny<TextureFormat?>(),
                It.IsAny<AttachmentLoadOp>(),
                It.IsAny<AttachmentLoadOp>()))
            .Returns(new Mock<IRenderPass3D>().Object);
        context.Setup(x => x.CreateFramebuffer3D(
                It.IsAny<IRenderPass3D>(),
                It.IsAny<IReadOnlyList<ITexture2D>>(),
                It.IsAny<ITexture2D?>()))
            .Returns(new Mock<IFramebuffer3D>().Object);
        context.Setup(x => x.CreatePipeline3D(
                It.IsAny<IRenderPass3D>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<DescriptorBinding[]>(),
                It.IsAny<VertexInputDescription>(),
                It.IsAny<PipelineOptions?>()))
            .Returns(new Mock<IPipeline3D>().Object);
        context.Setup(x => x.CreateDescriptorSet(
                It.IsAny<IPipeline3D>(), It.IsAny<DescriptorPoolSize[]>()))
            .Returns(() => new Mock<IDescriptorSet>().Object);
        context.Setup(x => x.CreateSampler(
                It.IsAny<SamplerFilter>(),
                It.IsAny<SamplerFilter>(),
                It.IsAny<SamplerAddressMode>(),
                It.IsAny<SamplerAddressMode>()))
            .Returns(new Mock<ISampler>().Object);
        context.Setup(x => x.CreateTextureArray(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<TextureFormat>()))
            .Returns(new Mock<ITextureArray>().Object);
        context.Setup(x => x.CreateTextureCubeArray(
                It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<TextureFormat>()))
            .Returns(new Mock<ITextureCubeArray>().Object);
        context.Setup(x => x.CreateBuffer(
                It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns(() => new TestBuffer());
        return context;
    }

    private static Mock<IShaderCompiler> CreateShaderCompiler()
    {
        var compiler = new Mock<IShaderCompiler>();
        compiler.Setup(x => x.CompileToSpirv(
                It.IsAny<string>(), It.IsAny<ShaderStage>(), It.IsAny<string>()))
            .Returns([]);
        compiler.As<IDisposable>();
        return compiler;
    }

    private static int CountNativeAllocations(Mock<IGraphicsContext> context)
        => context.Invocations.Count(x => x.Method.Name.StartsWith("Create", StringComparison.Ordinal));

    private static void SetField<T>(object target, string fieldName, T value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

    private static T? GetField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (T?)field.GetValue(target);
    }

    private static void SetProperty<T>(object target, string propertyName, T value)
    {
        PropertyInfo property = target.GetType().GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        property.SetValue(target, value);
    }

    private sealed class TestBuffer : IBuffer
    {
        public ulong Size => 0;

        public void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged
        {
        }

        public IntPtr Map() => IntPtr.Zero;

        public void Unmap()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class LifecycleProbeRenderNode(IGraphicsContext context) : RenderNode3D(context)
    {
        public int InitializeCount { get; private set; }

        public int ResizeCount { get; private set; }

        public int DisposeCount { get; private set; }

        public void Execute()
            => ThrowIfNotInitialized();

        protected override void OnInitialize(int width, int height)
            => InitializeCount++;

        protected override void OnResize(int width, int height)
            => ResizeCount++;

        protected override void OnDispose()
            => DisposeCount++;
    }

    private sealed class TestTextureResource(Action dispose) : TextureSource.Resource
    {
        public override ITexture2D? GetTexture(
            IGraphicsContext graphicsContext,
            RenderIntent renderIntent,
            RenderPullPurpose pullPurpose,
            float surfaceDensity = 1)
        {
            return null;
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                dispose();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }

    private sealed class DisposalProbeDrawableRenderNode(Drawable.Resource drawable, Action dispose)
        : DrawableRenderNode(drawable)
    {
        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                dispose();

            base.OnDispose(disposing);
        }
    }
}
