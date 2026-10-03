using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public sealed class RendererDisposalFailureTests
{
    [Test]
    public void Dispose_AttemptsEveryAllocatedOwnerAndPreservesAllFailures()
    {
        var owners = new List<Owner>();
        Mock<IGraphicsContext> context = CreateContext(owners);
        var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);
        foreach (Owner owner in owners)
            owner.Failure = new InvalidOperationException($"dispose {owner.Kind} {owners.IndexOf(owner)}");

        Exception? failure = Assert.Catch(renderer.Dispose);
        renderer.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(owners.Select(o => o.Kind), Does.Contain(nameof(IDescriptorSet)));
            Assert.That(owners.Select(o => o.Kind), Does.Contain(nameof(IPipeline3D)));
            Assert.That(owners.Select(o => o.Kind), Does.Contain(nameof(IBuffer)));
            Assert.That(owners.Select(o => o.Kind), Does.Contain(nameof(ITextureArray)));
            Assert.That(owners.Select(o => o.Kind), Does.Contain(nameof(ITextureCubeArray)));
            Assert.That(owners.Select(o => o.Attempts), Is.All.EqualTo(1), "every owner is attempted exactly once");
            Assert.That(Flatten(failure), Is.EquivalentTo(owners.Select(o => o.Failure)));
        });
        context.Verify(c => c.Dispose(), Times.Never, "the context is borrowed");
    }

    [Test]
    public void FailedInitialize_RollsBackEveryChildEvenWhenEveryChildDisposalFails()
    {
        var owners = new List<Owner>();
        var allocationFailure = new InvalidOperationException("output allocation failed");
        Mock<IGraphicsContext> context = CreateContext(owners);
        int viewportTextures = 0;
        context.Setup(c => c.CreateTexture2D(32, 32, It.IsAny<TextureFormat>()))
            .Returns((int width, int height, TextureFormat format) =>
            {
                if (++viewportTextures == 10)
                    throw allocationFailure;
                return CreateTexture(owners, width, height, format);
            });
        var renderer = new Renderer3D(context.Object);
        // The compiler remains renderer-owned after an initialization failure.
        Owner compiler = owners.Single(o => o.Kind == nameof(IShaderCompiler));
        compiler.Failure = null;
        context.Invocations.Clear();
        foreach (Owner owner in owners)
            owner.FailOnDispose = owner != compiler;
        s_failNewOwners.Value = true;
        Exception? failure;
        try
        {
            failure = Assert.Catch(() => renderer.Initialize(32, 32));
        }
        finally
        {
            s_failNewOwners.Value = false;
        }

        Assert.Multiple(() =>
        {
            Assert.That(viewportTextures, Is.EqualTo(10));
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((0, 0)));
            Assert.That(compiler.Attempts, Is.Zero);
            Assert.That(owners.Where(o => o != compiler).Select(o => o.Attempts), Is.All.EqualTo(1));
            Assert.That(Flatten(failure), Is.EquivalentTo(
                owners.Where(o => o != compiler).Select(o => o.Failure).Append(allocationFailure)));
        });
        renderer.Dispose();
        Assert.That(owners.Select(o => o.Attempts), Is.All.EqualTo(1));
        context.Verify(c => c.Dispose(), Times.Never);
    }

    private static readonly AsyncLocal<bool> s_failNewOwners = new();

    private static Exception[] Flatten(Exception? failure) => failure switch
    {
        AggregateException aggregate => aggregate.Flatten().InnerExceptions.ToArray(),
        null => [],
        _ => [failure]
    };

    private static Mock<IGraphicsContext> CreateContext(List<Owner> owners)
    {
        var context = new Mock<IGraphicsContext> { DefaultValue = DefaultValue.Mock };
        var compiler = CreateOwned<IShaderCompiler>(owners);
        compiler.Setup(c => c.CompileToSpirv(It.IsAny<string>(), It.IsAny<ShaderStage>(), It.IsAny<string>()))
            .Returns(new byte[] { 1, 2, 3, 4 });
        context.Setup(c => c.CreateShaderCompiler()).Returns(compiler.Object);
        context.Setup(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns((int width, int height, TextureFormat format) => CreateTexture(owners, width, height, format));
        context.Setup(c => c.CreateTextureArray(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<TextureFormat>()))
            .Returns(() => CreateOwned<ITextureArray>(owners).Object);
        context.Setup(c => c.CreateTextureCubeArray(It.IsAny<int>(), It.IsAny<uint>(), It.IsAny<TextureFormat>()))
            .Returns(() => CreateOwned<ITextureCubeArray>(owners).Object);
        context.Setup(c => c.CreateTextureCube(It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(() => CreateOwned<ITextureCube>(owners).Object);
        context.Setup(c => c.CreateBuffer(It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns((ulong size, BufferUsage _, MemoryProperty _) => new OwnedBuffer(size, AddOwner(owners, nameof(IBuffer))));
        context.Setup(c => c.CreateRenderPass3D(It.IsAny<IReadOnlyList<TextureFormat>>(), It.IsAny<TextureFormat?>(),
                It.IsAny<AttachmentLoadOp>(), It.IsAny<AttachmentLoadOp>()))
            .Returns(() => CreateOwned<IRenderPass3D>(owners).Object);
        context.Setup(c => c.CreateFramebuffer3D(It.IsAny<IRenderPass3D>(), It.IsAny<IReadOnlyList<ITexture2D>>(),
                It.IsAny<ITexture2D?>()))
            .Returns(() => CreateOwned<IFramebuffer3D>(owners).Object);
        context.Setup(c => c.CreatePipeline3D(It.IsAny<IRenderPass3D>(), It.IsAny<byte[]>(), It.IsAny<byte[]>(),
                It.IsAny<DescriptorBinding[]>(), It.IsAny<VertexInputDescription>(), It.IsAny<PipelineOptions?>()))
            .Returns(() => CreateOwned<IPipeline3D>(owners).Object);
        context.Setup(c => c.CreateDescriptorSet(It.IsAny<IPipeline3D>(), It.IsAny<DescriptorPoolSize[]>()))
            .Returns(() => CreateOwned<IDescriptorSet>(owners).Object);
        context.Setup(c => c.CreateSampler(It.IsAny<SamplerFilter>(), It.IsAny<SamplerFilter>(),
                It.IsAny<SamplerAddressMode>(), It.IsAny<SamplerAddressMode>()))
            .Returns(() => CreateOwned<ISampler>(owners).Object);
        return context;
    }

    private static ITexture2D CreateTexture(List<Owner> owners, int width, int height, TextureFormat format)
    {
        Mock<ITexture2D> texture = CreateOwned<ITexture2D>(owners);
        texture.SetupGet(t => t.Width).Returns(width);
        texture.SetupGet(t => t.Height).Returns(height);
        texture.SetupGet(t => t.Format).Returns(format);
        return texture.Object;
    }

    private static Mock<T> CreateOwned<T>(List<Owner> owners) where T : class
    {
        Owner owner = AddOwner(owners, typeof(T).Name);
        var mock = new Mock<T> { DefaultValue = DefaultValue.Mock };
        mock.As<IDisposable>().Setup(d => d.Dispose()).Callback(owner.Dispose);
        return mock;
    }

    private static Owner AddOwner(List<Owner> owners, string kind)
    {
        var owner = new Owner(kind) { FailOnDispose = s_failNewOwners.Value };
        owners.Add(owner);
        return owner;
    }

    private sealed class Owner(string kind)
    {
        public string Kind { get; } = kind;
        public int Attempts { get; private set; }
        public bool FailOnDispose { get; set; }
        public Exception? Failure { get; set; }
        public void Dispose()
        {
            Attempts++;
            if (FailOnDispose)
                Failure ??= new InvalidOperationException($"dispose {Kind}");
            if (Failure is not null)
                throw Failure;
        }
    }

    private sealed class OwnedBuffer(ulong size, Owner owner) : IBuffer
    {
        public ulong Size => size;
        public void Upload<T>(ReadOnlySpan<T> data) where T : unmanaged { }
        public IntPtr Map() => IntPtr.Zero;
        public void Unmap() { }
        public void Dispose() => owner.Dispose();
    }
}
