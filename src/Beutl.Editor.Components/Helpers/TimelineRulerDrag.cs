using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using MouseFlags = Beutl.Editor.Components.Helpers.TimelineHelper.MouseFlags;

namespace Beutl.Editor.Components.Helpers;

// Drags the scene's start and end bars on a ruler, the same way in the timeline and the graph editor.
internal sealed class TimelineRulerDrag
{
    private TimeSpan _initialStart;
    private TimeSpan _initialDuration;

    // The bar under the point on the ruler, or SeekBarPressed when the point is on neither bar.
    public MouseFlags Press(Scene scene, double x, double y, double startingBarX, double endingBarX)
    {
        if (TimelineHelper.IsPointInTimelineScaleEndingMarker(x, y, endingBarX))
        {
            return MouseFlags.EndingBarMarkerPressed;
        }

        if (TimelineHelper.IsPointInTimelineScaleStartingMarker(x, y, startingBarX))
        {
            _initialStart = scene.Start;
            _initialDuration = scene.Duration;
            return MouseFlags.StartingBarMarkerPressed;
        }

        return MouseFlags.SeekBarPressed;
    }

    // Moves the bar that flag holds; false when it holds neither bar. The service keeps the scene at least
    // one frame long, and the change is recorded on release.
    public bool Move(MouseFlags flag, IEditorContext editorContext, Scene scene, TimeSpan pointerFrame)
    {
        switch (flag)
        {
            case MouseFlags.EndingBarMarkerPressed:
                editorContext.GetRequiredService<ISceneTimeRangeService>().UpdateEndDrag(scene, pointerFrame);
                return true;
            case MouseFlags.StartingBarMarkerPressed:
                editorContext.GetRequiredService<ISceneTimeRangeService>()
                    .UpdateStartDrag(scene, pointerFrame, _initialStart, _initialDuration);
                return true;
            default:
                return false;
        }
    }

    public static void Release(MouseFlags flag, IEditorContext editorContext)
    {
        if (flag == MouseFlags.EndingBarMarkerPressed)
        {
            editorContext.GetRequiredService<ISceneTimeRangeService>().CommitEndChange();
        }
        else if (flag == MouseFlags.StartingBarMarkerPressed)
        {
            editorContext.GetRequiredService<ISceneTimeRangeService>().CommitStartChange();
        }
    }
}
