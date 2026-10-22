using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.ProjectSystem;

// Setup that the SceneDrawable fixtures share. They stay separate because only some of their tests need Vulkan.
internal static class SceneDrawableTestHelpers
{
    // Adds an element that holds obj for the first second of scene, with its file under basePath.
    public static Element AddElement(Scene scene, string basePath, EngineObject obj, int zIndex = 0)
    {
        var element = new Element
        {
            Start = MediaTime.Zero,
            Length = MediaTime.FromSeconds(1),
            IsEnabled = true,
            ZIndex = zIndex,
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
            TargetDomain = new Rect(0, 0, inner.FrameSize.Width, inner.FrameSize.Height),
            CacheOptions = RenderCacheOptions.Disabled,
        });
}
