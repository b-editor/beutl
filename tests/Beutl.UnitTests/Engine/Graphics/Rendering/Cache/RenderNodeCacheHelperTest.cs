using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Cache;

public class RenderNodeCacheHelperTest
{
    [Test]
    public void CanCacheRecursive_ShouldReturnFalse_WhenCacheCannotCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursive_ShouldReturnTrue_WhenCacheCanCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void CanCacheRecursive_ShouldReturnFalse_WhenChildCountIsDifferent()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        containerNode.AddChild(new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null));

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursive_ShouldReturnFalse_WhenChildIsDifferent()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        containerNode.SetChild(0, new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null));

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursive(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursiveChildrenOnly_ShouldReturnFalse_WhenAnyChildCannotCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        containerNode.Cache.ReportRenderCount(3);

        // Act
        var result = RenderNodeCacheHelper.CanCacheRecursiveChildrenOnly(containerNode);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void CanCacheRecursiveChildrenOnly_ShouldReturnTrue_WhenAllChildrenCanCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);

        // Act
        bool result = RenderNodeCacheHelper.CanCacheRecursiveChildrenOnly(containerNode);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void ClearCache_ShouldInvalidateCache()
    {
        // Arrange
        using var node = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        using (var renderTarget = RenderTarget.CreateNull(100, 100))
        {
            node.Cache.StoreCache(renderTarget, new Rect(0, 0, 100, 100));
        }

        // Act
        RenderNodeCacheHelper.ClearCache(node);

        // Assert
        Assert.That(node.Cache.IsCached, Is.False);
    }

    [Test]
    public void ClearCache_ShouldInvalidateCache_WhenNodeIsContainerRenderNode()
    {
        // Arrange
        using var node = new ContainerRenderNode();
        using var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        using (var renderTarget = RenderTarget.CreateNull(100, 100))
        {
            childNode.Cache.StoreCache(renderTarget, new Rect(0, 0, 100, 100));
        }
        node.AddChild(childNode);

        // Act
        RenderNodeCacheHelper.ClearCache(node);

        // Assert
        Assert.That(childNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void MakeCache_ShouldCreateCache_WhenCacheIsEnabledAndCanCache()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(true, RenderCacheRules.Default);

        // Act
        RenderNodeCacheHelper.MakeCache(containerNode, cacheOptions);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.True);
    }

    [Test]
    public void MakeCache_ShouldNotCreateCache_WhenCacheIsDisabled()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(false, RenderCacheRules.Default);

        // Act
        RenderNodeCacheHelper.MakeCache(containerNode, cacheOptions);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void MakeCache_ShouldNotCreateCache_WhenCannotCacheChildren()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(true, RenderCacheRules.Default);

        // Act
        RenderNodeCacheHelper.MakeCache(containerNode, cacheOptions);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.False);
    }

    [Test]
    public void CreateDefaultCache_ShouldStoreCache_WhenCacheRulesMatch()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        childNode.Cache.ReportRenderCount(3);
        containerNode.Cache.ReportRenderCount(3);
        var cacheOptions = new RenderCacheOptions(true, new RenderCacheRules(10000, 1));

        // Act
        RenderNodeCacheHelper.CreateDefaultCache(containerNode, cacheOptions);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.True);
    }

    [Test]
    public void CreateDefaultCache_ShouldNotStoreCache_WhenCacheRulesDoNotMatch()
    {
        // Arrange
        var childNode = new EllipseRenderNode(new Rect(0, 0, 100, 100), Brushes.Resource.White, null);
        var containerNode = new ContainerRenderNode();
        containerNode.AddChild(childNode);
        var cacheOptions = new RenderCacheOptions(true, new RenderCacheRules(1, 1));

        // Act
        RenderNodeCacheHelper.CreateDefaultCache(containerNode, cacheOptions);

        // Assert
        Assert.That(containerNode.Cache.IsCached, Is.False);
    }
}
