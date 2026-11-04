using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.PropertyEditors.Views;

public static class EditorMenuHelper
{
    public static void AttachCopyPasteAndTemplateMenus(Control host, params FAMenuFlyout[] flyouts)
    {
        foreach (FAMenuFlyout flyout in flyouts)
        {
            CopyPasteMenuHelper.AddMenus(flyout, host);
            TemplateMenuHelper.AddMenus(flyout, host);
        }
    }

    // The items already in the flyout replace or clear the whole value, which is how the user repairs what could not
    // be loaded. They stay above the other menus and show only while needsRepair answers true.
    public static void AttachRepairMenus(Control host, FAMenuFlyout flyout, Func<bool> needsRepair)
    {
        Control[] repairItems = flyout.Items.OfType<Control>().ToArray();
        flyout.Items.Clear();
        AttachCopyPasteAndTemplateMenus(host, flyout);

        var separator = new FAMenuFlyoutSeparator();
        flyout.Items.Insert(0, separator);
        for (int i = 0; i < repairItems.Length; i++)
        {
            flyout.Items.Insert(i, repairItems[i]);
        }

        flyout.Opening += (_, _) =>
        {
            bool visible = needsRepair();
            separator.IsVisible = visible;
            foreach (Control item in repairItems)
            {
                item.IsVisible = visible;
            }
        };
    }
}
