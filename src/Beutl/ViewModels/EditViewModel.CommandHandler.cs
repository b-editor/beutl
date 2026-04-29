using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Media;
using Beutl.ProjectSystem;
using ExCSS;
using Microsoft.Extensions.Logging;

namespace Beutl.ViewModels;

public partial class EditViewModel : IContextCommandHandler
{
    // IContextCommandHandler を実装しない場合、以下のように ContextCommandAttribute を使用してコマンドを定義します。
    // [ContextCommand]
    // public void PlayPause(KeyEventArgs e)
    // {
    //     if (e.Source is TextBox)
    //         return;
    //     Player.PlayPause.Execute();
    //     e.Handled = true;
    // }
    //
    // [ContextCommand]
    // public void Next()
    // {
    //     Player.Next.Execute();
    // }
    //
    // [ContextCommand]
    // public void Previous()
    // {
    //     Player.Previous.Execute();
    // }
    //
    // [ContextCommand]
    // public void SeekStart()
    // {
    //     Player.Start.Execute();
    // }
    //
    // [ContextCommand]
    // public void SeekEnd()
    // {
    //     Player.End.Execute();
    // }

    public void Execute(ContextCommandExecution execution)
    {
        if (execution.KeyEventArgs != null)
            execution.KeyEventArgs.Handled = true;
        switch (execution.CommandName)
        {
            case "PlayPause" when execution.KeyEventArgs?.Source is not TextBox:
                Player.PlayPause.Execute();
                break;
            case "Next":
                Player.Next.Execute();
                break;
            case "Previous":
                Player.Previous.Execute();
                break;
            case "SeekStart":
                Player.Start.Execute();
                break;
            case "SeekEnd":
                Player.End.Execute();
                break;
            case "ToggleMarker" when execution.KeyEventArgs?.Source is not TextBox:
                ToggleMarkerAtCurrentTime();
                break;
            case "NextMarker" when execution.KeyEventArgs?.Source is not TextBox:
                SeekToAdjacentMarker(forward: true);
                break;
            case "PreviousMarker" when execution.KeyEventArgs?.Source is not TextBox:
                SeekToAdjacentMarker(forward: false);
                break;
            default:
                if (execution.KeyEventArgs != null)
                    execution.KeyEventArgs.Handled = false;
                break;
        }
    }

    private void ToggleMarkerAtCurrentTime()
    {
        int rate = Player.GetFrameRate();
        TimeSpan time = _editorClock.CurrentTime.Value.RoundToRate(rate);
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;

        SceneMarker? existing = Scene.Markers
            .FirstOrDefault(m => m.Time.RoundToRate(rate) == time);
        if (existing != null)
        {
            Scene.Markers.Remove(existing);
            _logger.LogInformation("Removed marker at {Time}.", time);
        }
        else
        {
            Scene.Markers.Add(new SceneMarker(time, $"Marker {Scene.Markers.Count + 1}"));
            _logger.LogInformation("Added marker at {Time}.", time);
        }

        HistoryManager.Commit();
    }

    private void SeekToAdjacentMarker(bool forward)
    {
        TimeSpan current = _editorClock.CurrentTime.Value;
        TimeSpan? target = null;
        foreach (TimeSpan marker in Scene.Markers.Select(m => m.Time).Union([TimeSpan.Zero, Scene.Start, Scene.Duration + Scene.Start]))
        {
            if (forward)
            {
                if (marker > current
                    && (target == null || marker < target.Value))
                {
                    target = marker;
                }
            }
            else
            {
                if (marker < current
                    && (target == null || marker > target.Value))
                {
                    target = marker;
                }
            }
        }

        if (target != null)
        {
            _editorClock.CurrentTime.Value = target.Value;

            // ターゲットの位置までタイムラインを横スクロールさせる
            if (FindToolTab<TimelineTabViewModel>() is { } timeline)
            {
                int currentZIndex = timeline.ToLayerNumber(timeline.Options.Value.Offset.Y);
                timeline.ScrollTo.Execute((new TimeRange(target.Value, TimeSpan.FromTicks(1)), currentZIndex));
            }
        }
    }
}
