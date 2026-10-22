using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.Helpers;

// Where an element goes when no timeline is open to say where it was clicked: at the playhead, on
// the layer above everything showing there, like a drop on the player.
internal static class PlayheadPlacement
{
    public static (MediaTime Start, int Layer) Find(IEditorContext editorContext, Scene scene)
    {
        MediaTime time = editorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? MediaTime.Zero;
        MediaTime start = time.RoundToFrame(scene.FindHierarchicalParent<Project>().GetFrameRate(), MidpointRounding.AwayFromZero);
        int layer = scene.Children
            .Where(e => e.Start <= start && start < e.Range.End)
            .Select(e => e.ZIndex + 1)
            .DefaultIfEmpty(0)
            .Max();
        return (start, layer);
    }
}
