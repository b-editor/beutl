using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.VersionControl.ViewModels;

// AsyncReactiveCommand awaits its subscriptions from async void, so a failure that escapes one goes
// unhandled on the UI thread. Version-control commands whose work can fail subscribe through here.
internal static class VersionControlCommandBoundary
{
    internal static async Task RunAsync(
        Func<Task> operation,
        ILogger logger,
        string commandName)
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
            logger.LogError(ex, "The version-control command {Command} failed.", commandName);
            NotificationService.ShowError(Strings.VersionControl_ErrorTitle, ex.Message);
        }
    }
}
