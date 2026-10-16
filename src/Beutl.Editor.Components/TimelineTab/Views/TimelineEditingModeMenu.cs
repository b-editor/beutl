using Avalonia.Data;
using Beutl.Editor.Components.Helpers;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.TimelineTab.Views;

// The items of the Editing Mode submenu that the timeline's and the clips' context menus share. timelinePath is
// the binding path from the menu's data context to the TimelineTabViewModel.
internal static class TimelineEditingModeMenu
{
    public static void Populate(FAMenuFlyoutSubItem menu, string timelinePath)
    {
        menu.Items.Add(CreateToggle(timelinePath, "IsRazorMode", "timeline.toggleRazorMode", Strings.RazorTool, Icon.Cut));
        menu.Items.Add(CreateToggle(timelinePath, "IsSlipMode", "timeline.toggleSlipMode", Strings.SlipTool));
        menu.Items.Add(CreateToggle(timelinePath, "IsRollMode", "timeline.toggleRollMode", Strings.RollTool));
        menu.Items.Add(CreateToggle(timelinePath, "IsSlideMode", "timeline.toggleSlideMode", Strings.SlideTool));
        menu.Items.Add(new FAMenuFlyoutSeparator());
        menu.Items.Add(CreateToggle(timelinePath, "IsRippleEnabled", "timeline.toggleRippleMode", Strings.RippleEdit));
    }

    private static FAToggleMenuFlyoutItem CreateToggle(
        string timelinePath, string property, string commandId, string text, Icon? icon = null)
    {
        var item = new FAToggleMenuFlyoutItem
        {
            InputGesture = GetCommandGestureExtension.GetKeyGesture(typeof(TimelineTabExtension), commandId),
            Text = text
        };
        if (icon is { } value)
        {
            item.IconSource = new FluentIconSource { Icon = value };
        }

        item.Bind(
            FAToggleMenuFlyoutItem.IsCheckedProperty,
            new ReflectionBinding($"{timelinePath}{property}.Value") { Mode = BindingMode.TwoWay });
        return item;
    }
}
