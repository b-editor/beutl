using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.ProjectSystem;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.ProjectSystem;

// Setup that the SceneDrawable fixtures share. They stay separate because only some of their tests need Vulkan.
internal static class SceneDrawableTestHelpers
{
    public static SceneDrawable CreateDrawable(Scene target)
    {
        var drawable = new SceneDrawable();
        var owner = new Scene();
        owner.Children.Add(new Element { Objects = { drawable } });
        drawable.ReferencedScene.Expression = SceneReferenceTestHelper.Create(owner, target);
        return drawable;
    }

    // Adds an element that holds obj for the first second of scene, with its file under basePath.
    public static Element AddElement(Scene scene, string basePath, EngineObject obj, int zIndex = 0)
    {
        var element = new Element
        {
            Start = { Value = MediaTime.Zero },
            Length = { Value = MediaTime.FromSeconds(1) },
            IsEnabled = { Value = true },
            ZIndex = { Value = zIndex },
            Uri = new Uri(Path.Combine(basePath, $"{Guid.NewGuid():N}.layer"))
        };
        element.AddObject(obj);
        scene.Children.Add(element);
        return element;
    }

    // A preview renderer without caching over the frame of the nested scene that root recorded.
    public static RenderNodeRenderer CreateRenderer(RenderNode root, Scene inner)
        => new(root, new RenderNodeRenderRequest
        {
            Intent = RenderIntent.Preview,
            TargetDomain = new Rect(0, 0, inner.FrameSize.Value.Width, inner.FrameSize.Value.Height),
            CacheOptions = RenderCacheOptions.Disabled,
        });
}
