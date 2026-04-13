using Avalonia.Controls;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Editors;

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
}
