using Beutl.Graphics.Backend;
using Beutl.Graphics3D;
using Moq;

namespace Beutl.UnitTests.Engine.Graphics3D;

[TestFixture]
public sealed class RendererResizeOwnershipTests
{
    [TestCase(1)]
    [TestCase(6)]
    [TestCase(9)]
    [TestCase(10)]
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
            Assert.That(committed, Has.Length.EqualTo(10), "the complete original viewport was allocated");
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
            Assert.That(textures.Count(t => t.Width == 32), Is.EqualTo(10));
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
    public void AFailedResize_PreservesTheAllocationFailureWhenReplacementCleanupThrows()
    {
        var textures = new List<AllocatedTexture>();
        int replacementAllocation = 0;
        Mock<IGraphicsContext> context = CreateContext((width, height, format) =>
        {
            if (width == 32 && ++replacementAllocation == 10)
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

        Exception? failure = Assert.Catch(() => renderer.Resize(32, 32));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure, Is.TypeOf<AggregateException>());
            string[] messages = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions.Select(e => e.Message).ToArray()
                : failure is null ? [] : [failure.Message];
            Assert.That(messages, Is.EquivalentTo(new[]
            {
                "replacement allocation failed", "replacement depth disposal failed"
            }));
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((16, 16)));
            foreach (AllocatedTexture texture in textures.Where(t => t.Width == 16))
                texture.Mock.Verify(t => t.Dispose(), Times.Never);
            foreach (AllocatedTexture texture in textures.Where(t => t.Width == 32))
                texture.Mock.Verify(t => t.Dispose(), Times.Once);
        }
    }

    [Test]
    public void AFailedInitialize_AttemptsAllOwnersAndPreservesTheAllocationFailureWhenCleanupThrows()
    {
        var textures = new List<AllocatedTexture>();
        int viewportAllocation = 0;
        var allocationFailure = new InvalidOperationException("initial output allocation failed");
        var cleanupFailure = new InvalidOperationException("initial depth disposal failed");
        Mock<IGraphicsContext> context = CreateContext((width, height, format) =>
        {
            if (width == 32 && ++viewportAllocation == 10)
                throw allocationFailure;
            ITexture2D texture = AllocateTexture(textures, width, height, format);
            if (width == 32 && format == TextureFormat.Depth32Float && viewportAllocation == 5)
                textures[^1].Mock.Setup(t => t.Dispose()).Throws(cleanupFailure);
            return texture;
        });
        using var renderer = new Renderer3D(context.Object);

        Exception? failure = Assert.Catch(() => renderer.Initialize(32, 32));
        int[] disposalAttempts = textures.Select(t => t.Mock.Invocations
            .Count(i => i.Method.Name == nameof(IDisposable.Dispose))).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewportAllocation, Is.EqualTo(10), "the failure occurs after the viewport passes are allocated");
            Assert.That(textures.Any(t => t.Width != 32), Is.True, "fixed shadow textures were allocated first");
            Assert.That(failure, Is.TypeOf<AggregateException>());
            Exception[] failures = failure is AggregateException aggregate
                ? aggregate.Flatten().InnerExceptions.ToArray()
                : failure is null ? [] : [failure];
            Assert.That(failures, Is.EquivalentTo(new[] { allocationFailure, cleanupFailure }),
                "the original exception objects must both survive rollback");
            Assert.That((renderer.Width, renderer.Height), Is.EqualTo((0, 0)));
            Assert.That(disposalAttempts, Is.All.EqualTo(1), "cleanup must attempt every allocated pass owner");
        }
    }

    private static ITexture2D AllocateTexture(List<AllocatedTexture> textures, int width, int height, TextureFormat format)
    {
        var texture = new Mock<ITexture2D>();
        texture.SetupGet(t => t.Width).Returns(width);
        texture.SetupGet(t => t.Height).Returns(height);
        texture.SetupGet(t => t.Format).Returns(format);
        textures.Add(new AllocatedTexture(width, texture));
        return texture.Object;
    }

    private static Mock<IGraphicsContext> CreateContext(Func<int, int, TextureFormat, ITexture2D> allocate)
    {
        var context = new Mock<IGraphicsContext> { DefaultValue = DefaultValue.Mock };
        context.Setup(c => c.CreateTexture2D(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<TextureFormat>()))
            .Returns(allocate);
        context.Setup(c => c.CreateBuffer(It.IsAny<ulong>(), It.IsAny<BufferUsage>(), It.IsAny<MemoryProperty>()))
            .Returns((ulong size, BufferUsage _, MemoryProperty _) => new StubBuffer(size));
        return context;
    }

    private sealed record AllocatedTexture(int Width, Mock<ITexture2D> Mock);

    private sealed class StubBuffer(ulong size) : IBuffer
    {
        public ulong Size => size;

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
}
