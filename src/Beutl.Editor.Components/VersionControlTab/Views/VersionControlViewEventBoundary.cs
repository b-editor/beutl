using Beutl.Language;
using Beutl.Services;

namespace Beutl.Editor.Components.VersionControlTab.Views;

internal static class VersionControlViewEventBoundary
{
    internal static Task RunSafelyAsync(Func<Task> operation)
    {
        return RunSafelyAsync(
            operation,
            static exception => NotificationService.ShowError(
                Strings.VersionControl_ErrorTitle,
                exception.Message));
    }

    internal static async Task RunSafelyAsync(
        Func<Task> operation,
        Action<Exception> reportException)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            reportException(ex);
        }
    }
}
