using Beutl.Graphics.Backend;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics.Backend;

[TestFixture]
public sealed class MultisampleInterfaceTests
{
    private const TextureFormat ColorFormat = TextureFormat.RGBA16Float;
    private const TextureFormat DepthFormat = TextureFormat.Depth32Float;
    private static readonly TextureFormat[] s_colorFormats = [ColorFormat];

    [Test]
    public void SingleSampleDefaults_ForwardToLegacyMethods()
    {
        var context = new Mock<IGraphicsContext> { CallBase = true };
        var texture = new Mock<ITexture2D> { CallBase = true };
        var pass = new Mock<IRenderPass3D> { CallBase = true };
        context.Setup(c => c.CreateTexture2D(8, 16, ColorFormat)).Returns(texture.Object);
        context.Setup(c => c.CreateRenderPass3D(s_colorFormats, DepthFormat, AttachmentLoadOp.Load, AttachmentLoadOp.Clear))
            .Returns(pass.Object);

        Assert.That(context.Object.CreateTexture2D(8, 16, ColorFormat, 1), Is.SameAs(texture.Object));
        Assert.That(CreatePass(context.Object, 1), Is.SameAs(pass.Object));
        Assert.That(texture.Object.SampleCount, Is.EqualTo(1));
        Assert.That(pass.Object.SampleCount, Is.EqualTo(1));
        Assert.That(context.Object.GetSupportedSampleCount(4, s_colorFormats), Is.EqualTo(1));
        context.Verify(c => c.CreateTexture2D(8, 16, ColorFormat), Times.Once);
        context.Verify(c => c.CreateRenderPass3D(s_colorFormats, DepthFormat, AttachmentLoadOp.Load, AttachmentLoadOp.Clear),
            Times.Once);
    }

    [TestCase(2)]
    [TestCase(64)]
    public void LegacyContexts_RejectValidMultisampleCounts(int sampleCount)
    {
        IGraphicsContext context = new Mock<IGraphicsContext> { CallBase = true }.Object;

        Assert.That(() => context.CreateTexture2D(8, 16, ColorFormat, sampleCount), Throws.TypeOf<NotSupportedException>());
        Assert.That(() => CreatePass(context, sampleCount), Throws.TypeOf<NotSupportedException>());
    }

    [TestCase(-2)]
    [TestCase(0)]
    [TestCase(3)]
    [TestCase(65)]
    [TestCase(128)]
    public void InvalidSampleCounts_AreRejectedBeforeDelegating(int sampleCount)
    {
        IGraphicsContext context = new Mock<IGraphicsContext> { CallBase = true }.Object;

        Assert.That(() => context.CreateTexture2D(8, 16, ColorFormat, sampleCount),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("sampleCount"));
        Assert.That(() => CreatePass(context, sampleCount),
            Throws.TypeOf<ArgumentOutOfRangeException>().With.Property("ParamName").EqualTo("sampleCount"));
    }

    private static IRenderPass3D CreatePass(IGraphicsContext context, int sampleCount)
        => context.CreateRenderPass3D(s_colorFormats, DepthFormat, AttachmentLoadOp.Load, AttachmentLoadOp.Clear, sampleCount);
}
