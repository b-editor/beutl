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
        Assert.That(cache.CacheCount, Is.EqualTo(2));
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
        Assert.That(cache.CacheCount, Is.EqualTo(1));
    }
}
