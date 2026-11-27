using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.Helpers;

// Where an element goes when there is no timeline click to place it: at the playhead, on
// the layer above everything showing there, like a drop on the player.
internal static class PlayheadPlacement
{
    public static (MediaTime Start, int Layer) Find(IEditorContext editorContext, Scene scene)
    {
        MediaTime time = editorContext.GetService<IEditorClock>()?.CurrentTime.Value ?? MediaTime.Zero;
        MediaTime start = time.RoundToFrame(scene.GetFrameRate(), MidpointRounding.AwayFromZero);
        return (start, FindLayer(scene, start));
    }

    public static int FindLayer(Scene scene, MediaTime start)
        => scene.Children
            .Where(e => e.Start.Value <= start && start < e.Range.End)
            .Select(e => e.ZIndex.Value + 1)
            .DefaultIfEmpty(0)
            .Max();
}
