using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Moq;
using static Beutl.UnitTests.Engine.Graphics3D.Graphics3DTestDevices;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public sealed class RendererResizeOwnershipTests
{
    private const int SingleSampleTextureCount = 9;

    [TestCase(1)]
    [TestCase(6)]
    [TestCase(8)]
    [TestCase(SingleSampleTextureCount)]
    public void AFailedResize_KeepsTheCommittedTexturesAndReleasesItsReplacements(int failedAllocation)
    {
        var textures = new List<AllocatedTexture>();
        int replacementAllocation = 0;
        Mock<IGraphicsContext> context = CreateContext((width, height, format) =>
        {
            if (width == 32 && height == 32 && ++replacementAllocation == failedAllocation)
                throw new InvalidOperationException("replacement allocation failed");
            return AllocateTexture(textures, width, height, format);
        });
        using var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);
        AllocatedTexture[] committed = textures.Where(t => t.Width == 16).ToArray();

        Assert.That(
            () => renderer.Resize(32, 32),
            Throws.InvalidOperationException.With.Message.EqualTo("replacement allocation failed"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(committed, Has.Length.EqualTo(SingleSampleTextureCount), "the complete original viewport was allocated");
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((16, 16)));
            foreach (AllocatedTexture texture in committed)
                texture.Mock.Verify(t => t.Dispose(), Times.Never, "the original viewport must remain usable");
            foreach (AllocatedTexture texture in textures.Where(t => t.Width == 32))
                texture.Mock.Verify(t => t.Dispose(), Times.Once, "failed replacements have no owner after the exception");
        }

        // Returning to the committed extent can be a no-op only because its resources still exist.
        renderer.Resize(16, 16);
        foreach (AllocatedTexture texture in committed)
            texture.Mock.Verify(t => t.Dispose(), Times.Never);
    }

    [Test]
    public void AResize_AllocatesAllReplacementsBeforeReleasingTheCommittedTextures()
    {
        var textures = new List<AllocatedTexture>();
        AllocatedTexture[] committed = [];
        Mock<IGraphicsContext> context = CreateContext((width, height, format) =>
        {
            if (width == 32)
            {
                foreach (AllocatedTexture texture in committed)
                    texture.Mock.Verify(t => t.Dispose(), Times.Never,
                        "allocation can still fail, so the committed viewport must remain owned");
            }
            return AllocateTexture(textures, width, height, format);
        });
        using var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);
        committed = textures.Where(t => t.Width == 16).ToArray();

        renderer.Resize(32, 32);

        using (Assert.EnterMultipleScope())
        {
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((32, 32)));
            Assert.That(textures.Count(t => t.Width == 32), Is.EqualTo(SingleSampleTextureCount));
            foreach (AllocatedTexture texture in committed)
                texture.Mock.Verify(t => t.Dispose(), Times.Once);
            foreach (AllocatedTexture texture in textures.Where(t => t.Width == 32))
                texture.Mock.Verify(t => t.Dispose(), Times.Never);
        }
    }

    [Test]
    public void AResize_KeepsTheReplacementOwnedWhenRetiringAnOldTextureThrows()
    {
        var textures = new List<AllocatedTexture>();
        Mock<IGraphicsContext> context = CreateContext((width, height, format) =>
            AllocateTexture(textures, width, height, format));
        using var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);
        AllocatedTexture[] committed = textures.Where(t => t.Width == 16).ToArray();
        committed.First(t => t.Mock.Object.Format == TextureFormat.Depth32Float).Mock
            .Setup(t => t.Dispose()).Throws(new InvalidOperationException("old depth disposal failed"));

        Assert.That(
            () => renderer.Resize(32, 32),
            Throws.InvalidOperationException.With.Message.EqualTo("old depth disposal failed"));

        int[] retirementAttempts = committed.Select(t => t.Mock.Invocations
            .Count(i => i.Method.Name == nameof(IDisposable.Dispose))).ToArray();
        renderer.Dispose();

        using (Assert.EnterMultipleScope())
        {
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((32, 32)));
            Assert.That(retirementAttempts, Is.All.EqualTo(1), "retirement must attempt every old owner");
            foreach (AllocatedTexture texture in textures.Where(t => t.Width == 32))
                texture.Mock.Verify(t => t.Dispose(), Times.Once, "successful replacements must retain an owner");
        }
    }

    [Test]
    public void AFailedResize_ReportsTheCleanupFailureWhenReplacementCleanupThrows()
    {
        var textures = new List<AllocatedTexture>();
        int replacementAllocation = 0;
        Mock<IGraphicsContext> context = CreateContext((width, height, format) =>
        {
            if (width == 32 && ++replacementAllocation == SingleSampleTextureCount)
                throw new InvalidOperationException("replacement allocation failed");
            ITexture2D texture = AllocateTexture(textures, width, height, format);
            if (width == 32 && format == TextureFormat.Depth32Float && replacementAllocation == 5)
            {
                textures[^1].Mock.Setup(t => t.Dispose())
                    .Throws(new InvalidOperationException("replacement depth disposal failed"));
            }
            return texture;
        });
        using var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);

        // The cleanup failure replaces the allocation failure it followed.
        Exception? failure = Assert.Catch(() => renderer.Resize(32, 32));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure?.Message, Is.EqualTo("replacement depth disposal failed"));
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((16, 16)));
            foreach (AllocatedTexture texture in textures.Where(t => t.Width == 16))
                texture.Mock.Verify(t => t.Dispose(), Times.Never);
            foreach (AllocatedTexture texture in textures.Where(t => t.Width == 32))
                texture.Mock.Verify(t => t.Dispose(), Times.Once);
        }
    }

    [Test]
    public void AChangedSampleRequest_ReplacesTheViewportAtTheSameExtent()
    {
        var textures = new List<AllocatedTexture>();
        Mock<IGraphicsContext> context = CreateContext((width, height, format) => AllocateTexture(textures, width, height, format));
        context.Setup(c => c.GetSupportedSampleCount(4, It.IsAny<IReadOnlyList<TextureFormat>>())).Returns(4);
        context.Setup(c => c.CreateTexture2D(16, 16, It.IsAny<TextureFormat>(), 4))
            .Returns((int width, int height, TextureFormat format, int samples) => AllocateTexture(textures, width, height, format, samples));
        using var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);
        AllocatedTexture[] committed = textures.Where(t => t.Width == 16).ToArray();
        context.Verify(c => c.GetSupportedSampleCount(It.IsAny<int>(), It.IsAny<IReadOnlyList<TextureFormat>>()), Times.Never);

        renderer.Resize(16, 16, sampleCount: 4);

        Assert.That(renderer.SampleCount, Is.EqualTo(4));
        Assert.That(renderer.LastSampleAllocationFailure, Is.Null);
        AllocatedTexture[] replacements = textures.Where(t => t.Width == 16).Except(committed).ToArray();
        Assert.That(replacements.Count(t => t.Mock.Object.SampleCount == 4), Is.EqualTo(6), "G-buffer, depth and lit color");
        Assert.That(replacements.Count(t => t.Mock.Object.SampleCount == 1), Is.EqualTo(4), "resolved depth and output");
        foreach (AllocatedTexture texture in committed)
            texture.Mock.Verify(t => t.Dispose(), Times.Once);
        foreach (AllocatedTexture texture in replacements)
            texture.Mock.Verify(t => t.Dispose(), Times.Never);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FailedMultisampleAllocations_RetryInOrderAndKeepTheCurrentViewportUntilSuccess(bool failAtOneSample)
    {
        var textures = new List<AllocatedTexture>();
        bool replacing = false;
        Mock<IGraphicsContext> context = CreateContext((width, height, format) =>
        {
            if (replacing && failAtOneSample)
                throw new InvalidOperationException("single-sample allocation failed");
            return AllocateTexture(textures, width, height, format);
        });
        context.Setup(c => c.GetSupportedSampleCount(4, It.IsAny<IReadOnlyList<TextureFormat>>())).Returns(4);
        var counts = new List<int>();
        var multisampleFailure = new InvalidOperationException("multisample allocation failed");
        context.Setup(c => c.CreateTexture2D(16, 16, It.IsAny<TextureFormat>(), It.IsAny<int>()))
            .Returns((int width, int height, TextureFormat format, int samples) =>
            {
                if (counts.Count > 0 && counts[^1] == samples)
                    throw multisampleFailure;
                counts.Add(samples);
                return AllocateTexture(textures, width, height, format, samples);
            });
        using var renderer = new Renderer3D(context.Object);
        renderer.Initialize(16, 16);
        AllocatedTexture[] committed = textures.Where(t => t.Width == 16).ToArray();
        replacing = true;

        if (failAtOneSample)
        {
            Assert.That(() => renderer.Resize(16, 16, sampleCount: 4),
                Throws.InvalidOperationException.With.Message.EqualTo("single-sample allocation failed"));
            Assert.That(renderer.RequestedSampleCount, Is.EqualTo(1));
            Assert.That(renderer.LastSampleAllocationFailure, Is.Null);
        }
        else
        {
            renderer.Resize(16, 16, sampleCount: 4);
            Assert.That(renderer.RequestedSampleCount, Is.EqualTo(4));
            Assert.That(renderer.LastSampleAllocationFailure, Is.SameAs(multisampleFailure));
            renderer.Resize(16, 16, sampleCount: 4);
        }

        Assert.That(renderer.SampleCount, Is.EqualTo(1));
        Assert.That(counts, Is.EqualTo(new[] { 4, 2 }));
        foreach (AllocatedTexture texture in textures.Where(t => t.Mock.Object.SampleCount > 1))
            texture.Mock.Verify(t => t.Dispose(), Times.Once);
        foreach (AllocatedTexture texture in committed)
            texture.Mock.Verify(t => t.Dispose(), failAtOneSample ? Times.Never() : Times.Once());
        context.Verify(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>(), 1), Times.Never);
        context.Verify(c => c.GetSupportedSampleCount(It.IsAny<int>(), It.IsAny<IReadOnlyList<TextureFormat>>()), Times.Once);
        context.Verify(c => c.WaitIdle(), Times.Exactly(2));
    }

    [Test]
    public void AnOverLimitExtent_IsRefusedBeforeQueryingOrAllocatingMultisampling()
    {
        Mock<IGraphicsContext> context = LooseDevice(attachment: 4096, cube: 4096);
        using var renderer = new Renderer3D(context.Object, sampleCount: 4);

        Assert.That(() => renderer.Initialize(4097, 64), Throws.InvalidOperationException);

        context.Verify(c => c.GetSupportedSampleCount(It.IsAny<int>(), It.IsAny<IReadOnlyList<TextureFormat>>()), Times.Never);
        context.Verify(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>(), It.IsAny<int>()), Times.Never);
        context.Verify(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()), Times.Never);
    }

    private static ITexture2D AllocateTexture(
        List<AllocatedTexture> textures, int width, int height, TextureFormat format, int sampleCount = 1)
    {
        var texture = new Mock<ITexture2D>();
        texture.SetupGet(t => t.Width).Returns(width);
        texture.SetupGet(t => t.Height).Returns(height);
        texture.SetupGet(t => t.Format).Returns(format);
        texture.SetupGet(t => t.SampleCount).Returns(sampleCount);
        textures.Add(new AllocatedTexture(width, texture));
        return texture.Object;
    }

    private static Mock<IGraphicsContext> CreateContext(Func<int, int, TextureFormat, ITexture2D> allocate)
    {
        Mock<IGraphicsContext> context = LooseDevice();
        context.Setup(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(allocate);
        return context;
    }

    private sealed record AllocatedTexture(int Width, Mock<ITexture2D> Mock);
}
