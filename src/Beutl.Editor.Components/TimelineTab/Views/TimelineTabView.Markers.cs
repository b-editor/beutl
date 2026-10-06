using System.Numerics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Engine;
using Beutl.Logging;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings.Extensions;
using AvaColor = Avalonia.Media.Color;
using BtlColor = Beutl.Media.Color;
using MouseFlags = Beutl.Editor.Components.Helpers.TimelineHelper.MouseFlags;

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
            Time = marker.Time,
            MarkerName = marker.Name,
            Note = marker.Note,
            Color = AvaColor.FromArgb(marker.Color.A, marker.Color.R, marker.Color.G, marker.Color.B),
        };

        flyout.ValuesChanged += (_, values) =>
        {
            if (deleted) return;
            marker.Name = values.Name;
            marker.Note = values.Note;
            marker.Color = BtlColor.FromArgb(values.Color.A, values.Color.R, values.Color.G, values.Color.B);
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
