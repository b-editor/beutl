using Avalonia.Controls;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.Editor.Components.Helpers;

// The "Save as template" of the timeline's elements, the element property tab and the property editors.
internal static class TemplateSaving
{
    // What the timeline saves: one element as it is; several, a selection or a group, as a set that keeps their
    // timing, layers and groups.
    internal static ICoreSerializable CreateElementTemplate(Scene scene, IReadOnlyList<Element> elements)
        => elements.Count == 1 ? elements[0] : ElementSetFactory.Create(scene, elements);

    internal static SaveAsTemplateFlyout ShowSaveFlyout(INotificationService notifications,
        ObjectTemplateService templates, Control target, ICoreSerializable instance, string name, bool showAtPointer, Action? onSaved = null)
    {
        var flyout = new SaveAsTemplateFlyout { Text = name };
        flyout.Confirmed += async (_, confirmed) => await SaveAsync(notifications, templates, instance, confirmed, onSaved);
        flyout.ShowAt(target, showAtPointer);
        return flyout;
    }

    // A refused or failed save shows the same error; a cancelled one is not reported as a failure.
    internal static async Task<bool> SaveAsync(INotificationService notifications,
        ObjectTemplateService templates, ICoreSerializable instance, string? name, Action? onSaved = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await templates.AddFromInstanceAsync(instance, name, cancellationToken) is not null)
            {
                onSaved?.Invoke();
                return true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception)
        {
        }

        notifications.ShowError(Strings.SaveAsTemplate, MessageStrings.OperationFailed);
        return false;
    }
}
