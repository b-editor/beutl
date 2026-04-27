using Avalonia.Controls;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
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
            case "AddMarker" when execution.KeyEventArgs?.Source is not TextBox:
                AddMarkerAtCurrentTime();
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

    private SceneMarker AddMarkerAtCurrentTime()
    {
        var clock = (IEditorClock)GetService(typeof(IEditorClock))!;
        TimeSpan time = clock.CurrentTime.Value;
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;

        var marker = new SceneMarker(time, $"Marker {Scene.Markers.Count + 1}");
        Scene.Markers.Add(marker);
        _logger.LogInformation("Added marker at {Time}.", time);
        return marker;
    }

    private void SeekToAdjacentMarker(bool forward)
    {
        var clock = (IEditorClock)GetService(typeof(IEditorClock))!;
        TimeSpan current = clock.CurrentTime.Value;
        SceneMarker? target = null;
        foreach (SceneMarker marker in Scene.Markers)
        {
            if (forward)
            {
                if (marker.Time > current
                    && (target == null || marker.Time < target.Time))
                {
                    target = marker;
                }
            }
            else
            {
                if (marker.Time < current
                    && (target == null || marker.Time > target.Time))
                {
                    target = marker;
                }
            }
        }

        if (target != null)
        {
            clock.CurrentTime.Value = target.Time;
        }
    }
}
