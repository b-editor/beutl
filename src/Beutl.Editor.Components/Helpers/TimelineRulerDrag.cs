using Avalonia.Controls;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;
using MouseFlags = Beutl.Editor.Components.Helpers.TimelineHelper.MouseFlags;

namespace Beutl.Editor.Components.Helpers;

// Drags the scene's start and end bars on a ruler, the same way in the timeline and the graph editor.
// ended runs when the drag's session closes, so the view stops treating the pointer as holding a bar.
internal sealed class TimelineRulerDrag(Action ended)
{
    private TimeSpan _initialStart;
    private TimeSpan _initialDuration;
    private DragSession? _drag;

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

    // Moves the bar that flag holds inside the drag's session; false when it holds neither bar.
    public bool Move(MouseFlags flag, IEditorContext editorContext, Control ruler, Scene scene, TimeSpan pointerFrame)
    {
        bool endingBar = flag == MouseFlags.EndingBarMarkerPressed;
        if (!endingBar && flag != MouseFlags.StartingBarMarkerPressed)
            return false;

        _drag ??= endingBar
            ? DragSession.Begin(editorContext, ruler, CommandNames.ChangeSceneDuration, End, End)
            : DragSession.Begin(editorContext, ruler, CommandNames.ChangeSceneStart, End, End);
        if (_drag is null)
        {
            // Another gesture holds history.
            ended();
            return true;
        }

        var service = editorContext.GetRequiredService<ISceneTimeRangeService>();
        using (_drag.Enter())
        {
            if (endingBar)
                service.UpdateEndDrag(scene, pointerFrame);
            else
                service.UpdateStartDrag(scene, pointerFrame, _initialStart, _initialDuration);
        }

        return true;
    }

    public void Release() => _drag?.Confirm();

    private void End()
    {
        _drag = null;
        ended();
    }
}
