using Avalonia.Controls;
using Beutl.Controls;
using Beutl.Editor.Services;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.Editor.Components.Helpers;

// The "Save as template" of the timeline's elements, the element property tab and the property editors.
internal static class TemplateSaving
{
    internal static void ShowSaveFlyout(
        Control target, ICoreSerializable instance, string name, bool showAtPointer, Action? onSaved = null)
    {
        var flyout = new SaveAsTemplateFlyout { Text = name };
        flyout.Confirmed += async (_, confirmed) => await SaveAsync(instance, confirmed, onSaved);
        flyout.ShowAt(target, showAtPointer);
    }

    // A refused or failed save shows the same error; a cancelled one is not reported as a failure.
    internal static async Task<bool> SaveAsync(
        ICoreSerializable instance, string? name, Action? onSaved = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            if (await ObjectTemplateService.Instance.AddFromInstanceAsync(instance, name, cancellationToken) is not null)
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

        NotificationService.ShowError(Strings.SaveAsTemplate, MessageStrings.OperationFailed);
        return false;
    }
}
