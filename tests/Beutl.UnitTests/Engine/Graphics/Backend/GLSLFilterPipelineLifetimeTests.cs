using System.Reflection;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Shaders;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

public sealed class GLSLFilterPipelineLifetimeTests
{
    [TestCase(ShaderStage.Vertex)]
    [TestCase(ShaderStage.Fragment)]
    public void Create_WhenCompilationFails_DisposesCompiler(ShaderStage failingStage)
    {
        var fixture = new PipelineFixture();
        fixture.Compiler
            .Setup(x => x.CompileToSpirv(It.IsAny<string>(), failingStage, "main"))
            .Throws<InvalidOperationException>();

        GLSLFilterPipeline? result = fixture.Create();

        Assert.That(result, Is.Null);
        fixture.CompilerLifetime.Verify(x => x.Dispose(), Times.Once);
        fixture.Context.Verify(
            x => x.CreateRenderPass3D(
                It.IsAny<IReadOnlyList<TextureFormat>>(),
                It.IsAny<TextureFormat?>(),
                It.IsAny<AttachmentLoadOp>(),
                It.IsAny<AttachmentLoadOp>()),
            Times.Never);
    }

    [Test]
    public void Create_WhenSamplerCreationFails_DisposesRenderPassAndCompiler()
    {
        var fixture = new PipelineFixture();
        fixture.Context
            .Setup(x => x.CreateSampler(
                It.IsAny<SamplerFilter>(),
                It.IsAny<SamplerFilter>(),
                It.IsAny<SamplerAddressMode>(),
                It.IsAny<SamplerAddressMode>()))
            .Throws<InvalidOperationException>();

        GLSLFilterPipeline? result = fixture.Create();

        Assert.That(result, Is.Null);
        fixture.CompilerLifetime.Verify(x => x.Dispose(), Times.Once);
        fixture.RenderPass.Verify(x => x.Dispose(), Times.Once);
        fixture.Sampler.Verify(x => x.Dispose(), Times.Never);
        fixture.Pipeline.Verify(x => x.Dispose(), Times.Never);
    }

    [Test]
    public void Create_WhenPipelineCreationFails_DisposesSamplerRenderPassAndCompiler()
    {
        var fixture = new PipelineFixture();
        fixture.Context
            .Setup(x => x.CreatePipeline3D(
                It.IsAny<IRenderPass3D>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<DescriptorBinding[]>(),
                It.IsAny<VertexInputDescription>(),
                It.IsAny<PipelineOptions>()))
            .Throws<InvalidOperationException>();

        GLSLFilterPipeline? result = fixture.Create();

        Assert.That(result, Is.Null);
        fixture.CompilerLifetime.Verify(x => x.Dispose(), Times.Once);
        fixture.Sampler.Verify(x => x.Dispose(), Times.Once);
        fixture.RenderPass.Verify(x => x.Dispose(), Times.Once);
        fixture.Pipeline.Verify(x => x.Dispose(), Times.Never);
    }

    [Test]
    public void Create_WhenConstructionAndCleanupFail_ReturnsNullAfterEveryCleanup()
    {
        var fixture = new PipelineFixture();
        fixture.Context
            .Setup(x => x.CreatePipeline3D(
                It.IsAny<IRenderPass3D>(),
                It.IsAny<byte[]>(),
                It.IsAny<byte[]>(),
                It.IsAny<DescriptorBinding[]>(),
                It.IsAny<VertexInputDescription>(),
                It.IsAny<PipelineOptions>()))
            .Throws(new InvalidOperationException("construction"));
        fixture.RenderPass.Setup(x => x.Dispose()).Throws(new InvalidOperationException("render pass"));
        fixture.Sampler.Setup(x => x.Dispose()).Throws(new InvalidOperationException("sampler"));

        GLSLFilterPipeline? result = null;
        Assert.DoesNotThrow(() => result = fixture.Create());

        Assert.That(result, Is.Null);
        fixture.CompilerLifetime.Verify(x => x.Dispose(), Times.Once);
        fixture.RenderPass.Verify(x => x.Dispose(), Times.Once);
        fixture.Sampler.Verify(x => x.Dispose(), Times.Once);
        fixture.Pipeline.Verify(x => x.Dispose(), Times.Never);
    }

    [Test]
    public void Create_OnSuccess_TransfersResourcesToReturnedPipeline()
    {
        var fixture = new PipelineFixture();

        GLSLFilterPipeline? result = fixture.Create();

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.RetainedByteSize, Is.EqualTo(8));
        Assert.That(
            typeof(GLSLFilterPipeline)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Select(static field => field.FieldType),
            Has.None.EqualTo(typeof(byte[])));
        fixture.CompilerLifetime.Verify(x => x.Dispose(), Times.Once);
        fixture.Pipeline.Verify(x => x.Dispose(), Times.Never);
        fixture.RenderPass.Verify(x => x.Dispose(), Times.Never);
        fixture.Sampler.Verify(x => x.Dispose(), Times.Never);

        result.Dispose();
        result.Dispose();

        fixture.Pipeline.Verify(x => x.Dispose(), Times.Once);
        fixture.RenderPass.Verify(x => x.Dispose(), Times.Once);
        fixture.Sampler.Verify(x => x.Dispose(), Times.Once);
    }

    [TestCase(PipelineResource.Pipeline)]
    [TestCase(PipelineResource.RenderPass)]
    [TestCase(PipelineResource.Sampler)]
    public void Dispose_WhenResourceThrows_AttemptsEveryResourceOnce(PipelineResource failingResource)
    {
        var fixture = new PipelineFixture();
        switch (failingResource)
        {
            case PipelineResource.Pipeline:
                fixture.Pipeline.Setup(x => x.Dispose()).Throws<InvalidOperationException>();
                break;
            case PipelineResource.RenderPass:
                fixture.RenderPass.Setup(x => x.Dispose()).Throws<InvalidOperationException>();
                break;
            case PipelineResource.Sampler:
                fixture.Sampler.Setup(x => x.Dispose()).Throws<InvalidOperationException>();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(failingResource), failingResource, null);
        }

        GLSLFilterPipeline? result = fixture.Create();

        Assert.That(result, Is.Not.Null);
        Assert.Throws<InvalidOperationException>(result!.Dispose);
        Assert.DoesNotThrow(result.Dispose);
        fixture.Pipeline.Verify(x => x.Dispose(), Times.Once);
        fixture.RenderPass.Verify(x => x.Dispose(), Times.Once);
        fixture.Sampler.Verify(x => x.Dispose(), Times.Once);
    }

    [Test]
    public void Dispose_WhenEveryResourceThrows_PreservesEveryFailure()
    {
        var fixture = new PipelineFixture();
        var pipelineFailure = new InvalidOperationException("pipeline");
        var renderPassFailure = new InvalidOperationException("render pass");
        var samplerFailure = new InvalidOperationException("sampler");
        fixture.Pipeline.Setup(x => x.Dispose()).Throws(pipelineFailure);
        fixture.RenderPass.Setup(x => x.Dispose()).Throws(renderPassFailure);
        fixture.Sampler.Setup(x => x.Dispose()).Throws(samplerFailure);
        GLSLFilterPipeline result = fixture.Create()
            ?? throw new AssertionException("The fixture pipeline was not created.");

        AggregateException? failure = Assert.Throws<AggregateException>(result.Dispose);

        Assert.That(
            failure!.InnerExceptions,
            Is.EqualTo(new[] { pipelineFailure, renderPassFailure, samplerFailure }));
        Assert.DoesNotThrow(result.Dispose);
        fixture.Pipeline.Verify(x => x.Dispose(), Times.Once);
        fixture.RenderPass.Verify(x => x.Dispose(), Times.Once);
        fixture.Sampler.Verify(x => x.Dispose(), Times.Once);
    }

    private sealed class PipelineFixture
    {
        public PipelineFixture()
        {
            CompilerLifetime = Compiler.As<IDisposable>();

            Context.SetupGet(x => x.Supports3DRendering).Returns(true);
            Context.Setup(x => x.CreateShaderCompiler()).Returns(Compiler.Object);
            Compiler
                .Setup(x => x.CompileToSpirv(It.IsAny<string>(), It.IsAny<ShaderStage>(), "main"))
                .Returns([0x03, 0x02, 0x23, 0x07]);
            Context
                .Setup(x => x.CreateRenderPass3D(
                    It.IsAny<IReadOnlyList<TextureFormat>>(),
                    It.IsAny<TextureFormat?>(),
                    It.IsAny<AttachmentLoadOp>(),
                    It.IsAny<AttachmentLoadOp>()))
                .Returns(RenderPass.Object);
            Context
                .Setup(x => x.CreateSampler(
                    It.IsAny<SamplerFilter>(),
                    It.IsAny<SamplerFilter>(),
                    It.IsAny<SamplerAddressMode>(),
                    It.IsAny<SamplerAddressMode>()))
                .Returns(Sampler.Object);
            Context
                .Setup(x => x.CreatePipeline3D(
                    It.IsAny<IRenderPass3D>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<byte[]>(),
                    It.IsAny<DescriptorBinding[]>(),
                    It.IsAny<VertexInputDescription>(),
                    It.IsAny<PipelineOptions>()))
                .Returns(Pipeline.Object);
        }

        public Mock<IGraphicsContext> Context { get; } = new();

        public Mock<IShaderCompiler> Compiler { get; } = new();

        public Mock<IDisposable> CompilerLifetime { get; }

        public Mock<IRenderPass3D> RenderPass { get; } = new();

        public Mock<ISampler> Sampler { get; } = new();

        public Mock<IPipeline3D> Pipeline { get; } = new();

        public GLSLFilterPipeline? Create()
            => GLSLFilterPipeline.Create(
                Context.Object,
                "fragment shader",
                ShaderOutputCoverage.ProvablyFull);
    }

    public enum PipelineResource
    {
        Pipeline,
        RenderPass,
        Sampler,
    }
}
