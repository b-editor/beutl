using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using SelectionKind = Beutl.Editor.Components.TimelineTab.ViewModels.TimelineTabViewModel.ElementSelectionKind;

namespace Beutl.Editor.Components.TimelineTab.Views;

// Both context menus take a snapshot of their reference position when they open, so hovering the
// submenu does not change what the selection acts on.
internal static class TimelineSelectionMenu
{
    public static void Populate(FAMenuFlyoutSubItem menu, TimelineTabViewModel timeline,
        MediaTime time, int layer, Element? reference = null)
    {
        menu.Items.Clear();
        Add(Strings.SelectAll, timeline.SelectAll).InputGesture =
            GetCommandGestureExtension.GetKeyGesture(typeof(TimelineTabExtension), "timeline.selectAll");
        menu.Items.Add(new FAMenuFlyoutSeparator());

        AddSelection(Strings.SelectGroupElements, SelectionKind.Group).IsEnabled = timeline.CanSelectGroup(reference);
        AddSelection(Strings.SelectLayerElements, SelectionKind.Layer);
        AddSelection(Strings.SelectElementsAtPosition, SelectionKind.AtPosition);
        menu.Items.Add(new FAMenuFlyoutSeparator());

        AddSelection(Strings.SelectPrecedingElements, SelectionKind.Preceding);
        AddSelection(Strings.SelectFollowingElements, SelectionKind.Following);
        menu.Items.Add(new FAMenuFlyoutSeparator());

        AddSelection(Strings.SelectPrecedingElementsAllLayers, SelectionKind.PrecedingAllLayers);
        AddSelection(Strings.SelectFollowingElementsAllLayers, SelectionKind.FollowingAllLayers);
        menu.Items.Add(new FAMenuFlyoutSeparator());

        AddSelection(Strings.InvertSelection, SelectionKind.Invert);
        Add(Strings.DeselectAll, timeline.ClearSelected).InputGesture =
            GetCommandGestureExtension.GetKeyGesture(typeof(TimelineTabExtension), "timeline.deselectAll");

        FAMenuFlyoutItem AddSelection(string text, SelectionKind kind)
            => Add(text, () => timeline.SelectElements(kind, time, layer, reference));

        FAMenuFlyoutItem Add(string text, Action select)
        {
            var item = new FAMenuFlyoutItem { Text = text };
            item.Click += (_, _) =>
            {
                timeline.FlushPendingNudgeCommit();
                select();
            };
            menu.Items.Add(item);
            return item;
        }
    }
}
