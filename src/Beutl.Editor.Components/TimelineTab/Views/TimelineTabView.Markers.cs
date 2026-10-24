using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Views;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.TimelineTab.Views;

public sealed partial class TimelineTabView
{
    // ポインターがマーカー上にあるときは Scale の ContextFlyout を抑制する
    private void Scale_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.TryGetPosition(Scale, out Point position)
            && Scale.HitTestMarker(position.X, position.Y) != null)
        {
            e.Handled = true;
        }
    }

    private void ShowMarkerEditFlyout(SceneMarker marker)
    {
        if (ViewModel == null) return;

        var initialName = marker.Name.Value;
        var initialNote = marker.Note.Value;
        var initialColor = marker.Color.Value;
        bool deleted = false;

        var flyout = new MarkerEditFlyout
        {
            MarkerName = marker.Name.Value,
            Note = marker.Note.Value,
            Color = marker.Color.Value.ToAvaColor(),
            Placement = PlacementMode.BottomEdgeAlignedLeft,
        };

        flyout.ValuesChanged += (_, values) =>
        {
            if (deleted) return;
            marker.Name.Value = values.Name;
            marker.Note.Value = values.Note;
            marker.Color.Value = values.Color.ToBtlColor();
        };

        flyout.DeleteRequested += (_, _) =>
        {
            if (ViewModel == null) return;
            // 削除前にプロパティ変更が残っていれば確定しない（Removeコマンドだけを履歴に残す）
            marker.Name.Value = initialName;
            marker.Note.Value = initialNote;
            marker.Color.Value = initialColor;
            deleted = true;
            ViewModel.Scene.Markers.Remove(marker);
            ViewModel.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RemoveMarker);
        };

        flyout.Closed += (_, _) =>
        {
            if (deleted || ViewModel == null) return;
            if (marker.Name.Value != initialName
                || marker.Note.Value != initialNote
                || marker.Color.Value != initialColor)
            {
                ViewModel.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.EditMarker);
            }
        };

        // Anchor to the marker while allowing the native popup to resize as the note grows.
        double markerX = marker.Time.Value.TimeToPixel(Scale.Scale) - Scale.Offset.X;
        flyout.Popup.PlacementRect = new Rect(markerX, 0, 1, Scale.Bounds.Height);
        flyout.ShowAt(Scale);
    }
}
