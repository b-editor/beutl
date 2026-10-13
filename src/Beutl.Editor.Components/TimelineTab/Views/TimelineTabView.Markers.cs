using Avalonia;
using Avalonia.Input;
using Beutl.Controls;
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

        var initialName = marker.Name;
        var initialNote = marker.Note;
        var initialColor = marker.Color;
        bool deleted = false;

        var flyout = new MarkerEditFlyout
        {
            MarkerName = marker.Name,
            Note = marker.Note,
            Color = marker.Color.ToAvaColor(),
        };

        flyout.ValuesChanged += (_, values) =>
        {
            if (deleted) return;
            marker.Name = values.Name;
            marker.Note = values.Note;
            marker.Color = values.Color.ToBtlColor();
        };

        flyout.DeleteRequested += (_, _) =>
        {
            if (ViewModel == null) return;
            // 削除前にプロパティ変更が残っていれば確定しない（Removeコマンドだけを履歴に残す）
            marker.Name = initialName;
            marker.Note = initialNote;
            marker.Color = initialColor;
            deleted = true;
            ViewModel.Scene.Markers.Remove(marker);
            ViewModel.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.RemoveMarker);
        };

        flyout.Closed += (_, _) =>
        {
            if (deleted || ViewModel == null) return;
            if (marker.Name != initialName
                || marker.Note != initialNote
                || marker.Color != initialColor)
            {
                ViewModel.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.EditMarker);
            }
        };

        flyout.ShowAt(Scale, true);
    }
}
