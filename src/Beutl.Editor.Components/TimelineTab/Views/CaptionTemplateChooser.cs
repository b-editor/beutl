using Avalonia.Controls;
using Beutl.Editor.Services.Captions;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.TimelineTab.Views;

// Asks which look the clips of dropped subtitle files take, with a menu where they were dropped.
internal static class CaptionTemplateChooser
{
    public static Task<CaptionTemplate?> ShowAsync(Control target, IEnumerable<CaptionTemplate> templates)
    {
        var closed = new TaskCompletionSource<CaptionTemplate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CaptionTemplate? chosen = null;
        // The pointer rests on the first template, so taking it needs one click and no move.
        var menu = new FAMenuFlyout { HorizontalOffset = -12, VerticalOffset = -48 };
        menu.Items.Add(new FAMenuFlyoutItem { Text = Strings.AiSubtitle_Template, IsEnabled = false });
        foreach (CaptionTemplate template in templates)
        {
            var item = new FAMenuFlyoutItem { Text = template.Descriptor.Name };
            item.Click += (_, _) => chosen = template;
            menu.Items.Add(item);
        }

        // An item reports its click before the menu closes.
        menu.Closed += (_, _) => closed.TrySetResult(chosen);

        // A file dragged from another app leaves that app in front, where a click would only bring Beutl back.
        (TopLevel.GetTopLevel(target) as Window)?.Activate();
        menu.ShowAt(target, true);
        return closed.Task;
    }
}
