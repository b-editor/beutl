using Avalonia.Controls;
using Beutl.Editor.Services.Captions;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.Helpers;

internal sealed class CaptionTemplateChooser
{
    private CaptionTemplateId? _lastChoice;

    public Task<CaptionTemplate?> ChooseForPasteAsync(IReadOnlyList<CaptionTemplate> templates)
        => AppHelper.GetTopLevel() is { } target ? ChooseAsync(target, templates) : Task.FromResult<CaptionTemplate?>(templates[0]);

    public async Task<CaptionTemplate?> ChooseAsync(Control target, IReadOnlyList<CaptionTemplate> templates)
    {
        var closed = new TaskCompletionSource<CaptionTemplate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CaptionTemplate? chosen = null;
        // The pointer rests on the first template, so taking it needs one click and no move.
        var menu = new FAMenuFlyout { HorizontalOffset = -12, VerticalOffset = -48 };
        menu.Items.Add(new FAMenuFlyoutItem { Text = Strings.AiSubtitle_Template, IsEnabled = false });
        foreach (CaptionTemplate template in templates.OrderByDescending(item => item.Descriptor.Id == _lastChoice))
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
        CaptionTemplate? result = await closed.Task;
        if (result is not null) _lastChoice = result.Descriptor.Id;
        return result;
    }
}
