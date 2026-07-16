using System.Linq;
using System.Reflection;

using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class RenderNodeCacheTests
{
    [Test]
    [TestCase(3)]
    [TestCase(4)]
    public void ReportRenderCount_GreaterThanOrEqualToThree_ShouldSetCanCacheToTrue(int count)
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act
        cache.ReportRenderCount(count);

        // Assert
        Assert.That(cache.CanCache(), Is.True);
    }

    [Test]
    public void IncrementRenderCount_CalledThreeOrMoreTimes_ShouldSetCanCacheToTrue()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act
        cache.IncrementRenderCount();
        cache.IncrementRenderCount();
        cache.IncrementRenderCount();

        // Assert
        Assert.That(cache.CanCache(), Is.True);
    }

    [Test]
    public void UseCache_NotCached_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act & Assert
        Assert.Catch<Exception>(() => cache.UseCache(out _));
    }

    [Test]
    public void UseCache_NotCached_ShouldReturnEmptyArray()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act
        var result = cache.UseCache();

        // Assert
        Assert.That(result, Is.Empty);
    }

    [Test]
    public void StoreCache_Called_ShouldStoreCache()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        cache.StoreCache(renderTarget, new Rect(0, 0, 1, 1));

        // Assert
        Assert.That(cache.IsCached, Is.True);
    }

    [Test]
    public void StoreCache_CalledMultipleTimes_ShouldStoreMultipleCaches()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);

        // Act
        using var renderTarget1 = RenderTarget.CreateNull(1, 1);
        using var renderTarget2 = RenderTarget.CreateNull(1, 1);
        cache.StoreCache([(renderTarget1, new Rect(0, 0, 1, 1)), (renderTarget2, new Rect(0, 0, 1, 1))]);

        // Assert
        Assert.That(cache.IsCached, Is.True);
        Assert.That(cache.UseCache().Count(), Is.EqualTo(2));
    }

    [Test]
    public void StoreCache_WhenLaterCopyFails_PreservesExistingCacheAndDiscardsEveryStagedCopy()
    {
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);
        using var existing = RenderTarget.CreateNull(1, 1);
        using var valid = RenderTarget.CreateNull(1, 1);
        using var disposed = RenderTarget.CreateNull(1, 1);
        var existingBounds = new Rect(2, 3, 4, 5);
        cache.StoreCache(existing, existingBounds, RenderIntent.Preview, density: 2f);
        disposed.Dispose();
        cache.RejectCache(RenderIntent.Delivery);
        (RenderTarget RenderTarget, Rect Bounds)[] items =
        [
            (valid, new Rect(0, 0, 1, 1)),
            (disposed, new Rect(0, 0, 1, 1)),
        ];

        Assert.Throws<ObjectDisposedException>(() => cache.StoreCache(items, RenderIntent.Delivery));
        using RenderTarget retained = cache.UseCache(out Rect retainedBounds);

        Assert.Multiple(() =>
        {
            Assert.That(cache.CacheCount, Is.EqualTo(1));
            Assert.That(cache.CachedRenderIntent, Is.EqualTo(RenderIntent.Preview));
            Assert.That(cache.CachedPullPurpose, Is.EqualTo(RenderPullPurpose.Frame));
            Assert.That(cache.Density, Is.EqualTo(2f));
            Assert.That(retainedBounds, Is.EqualTo(existingBounds));
            Assert.That(cache.IsCacheRejectedFor(RenderIntent.Delivery), Is.True,
                "policy state must not commit until every shallow copy succeeds");
            Assert.That(GetSurfaceRefCount(valid), Is.EqualTo(1),
                "the shallow copy staged before the failure must be disposed");
        });
    }

    [Test]
    public void StoreCache_Called_ShouldInvalidateExistingCache()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);
        using (var renderTarget = RenderTarget.CreateNull(1, 1))
        {
            cache.StoreCache(renderTarget, new Rect(0, 0, 1, 1));
        }

        // Act
        using (var newRenderTarget = RenderTarget.CreateNull(1, 1))
        {
            cache.StoreCache(newRenderTarget, new Rect(0, 0, 1, 1));
        }

        // Assert
        Assert.That(cache.IsCached, Is.True);
        Assert.That(cache.UseCache().Count(), Is.EqualTo(1));
    }

    [Test]
    public void IncrementRenderCount_WhenNodeChanged_ShouldInvalidateExistingCache()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        node.Cache.StoreCache(renderTarget, new Rect(0, 0, 1, 1));
        node.Cache.ReportRenderCount(RenderNodeCache.Count);
        node.HasChanges = true;

        // Act
        node.Cache.IncrementRenderCount();

        // Assert
        Assert.That(node.Cache.IsCached, Is.False);
        Assert.That(node.Cache.CanCache(), Is.False);
        Assert.That(node.Cache.IsCacheRejected, Is.False);
    }

    [Test]
    public void PolicyAwareMembers_ValidateEnumsAndRejectAuxiliaryStorageWithoutSideEffects()
    {
        using var node = new ContainerRenderNode();
        using var cache = new RenderNodeCache(node);
        using var renderTarget = RenderTarget.CreateNull(1, 1);
        var invalidIntent = (RenderIntent)42;
        var invalidPurpose = (RenderPullPurpose)42;

        Assert.Multiple(() =>
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.IsCachedFor(invalidIntent));
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.IsCacheRejectedFor(
                RenderIntent.Preview,
                invalidPurpose));
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.RejectCache(invalidIntent));
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.StoreCache(
                renderTarget,
                new Rect(0, 0, 1, 1),
                invalidIntent));
            Assert.Throws<NotSupportedException>(() => cache.RejectCache(
                RenderIntent.Preview,
                RenderPullPurpose.Auxiliary));
            Assert.Throws<NotSupportedException>(() => cache.StoreCache(
                renderTarget,
                new Rect(0, 0, 1, 1),
                RenderIntent.Preview,
                pullPurpose: RenderPullPurpose.Auxiliary));
        });

        Assert.Multiple(() =>
        {
            Assert.That(cache.IsCached, Is.False);
            Assert.That(cache.IsCacheRejected, Is.False);
            Assert.That(cache.CachedRenderIntent, Is.Null);
            Assert.That(cache.CachedPullPurpose, Is.Null);
        });
    }

    private static int GetSurfaceRefCount(RenderTarget target)
    {
        object surfaceCounter = typeof(RenderTarget)
            .GetField("_surface", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;
        return (int)surfaceCounter.GetType()
            .GetProperty("RefCount", BindingFlags.Instance | BindingFlags.Public)!
            .GetValue(surfaceCounter)!;
    }
}
