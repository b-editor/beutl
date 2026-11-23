using Avalonia;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;

namespace Beutl.Editor.Components.TimelineTab.Views;

// What a timeline drag shows of its snapping: the snap bar at the target it reached, and an alignment pulse each time
// it reaches a new one. Only a drag's final preview reports here; commits, undo and its intermediate calculations do
// not.
internal sealed class TimelineSnapFeedback
{
    private MediaTime? _lastTarget;

    public void Update(TimelineTabViewModel timeline, Visual source, MediaTime? target, float scale, ulong timestamp)
    {
        timeline.SnapBarPosition.Value = target?.TimeToPixel(scale);
        MediaTime? previous = _lastTarget;
        _lastTarget = target;
        if (target.HasValue && target != previous)
        {
            NativeHapticFeedback.PerformAlignment(source, timestamp);
        }
    }

    public void Clear(TimelineTabViewModel timeline)
    {
        _lastTarget = null;
        timeline.SnapBarPosition.Value = null;
    }
}
