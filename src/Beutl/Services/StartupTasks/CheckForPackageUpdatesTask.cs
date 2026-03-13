using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Logging;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.StartupTasks;

public sealed class CheckForPackageUpdatesTask : StartupTask
{
    private readonly ILogger<CheckForPackageUpdatesTask> _logger = Log.CreateLogger<CheckForPackageUpdatesTask>();

    public CheckForPackageUpdatesTask(Startup startup, PackageManager packageManager)
    {
        Task = Task.Run(async () =>
        {
            using (Activity? activity = Telemetry.StartActivity("CheckForPackageUpdatesTask"))
            {
                await startup.WaitLoadingExtensions();

                activity?.AddEvent(new("Checking for package updates"));

                try
                {
                    IReadOnlyList<PackageUpdate> updates = await packageManager.CheckUpdate();
                    activity?.SetTag("UpdateCount", updates.Count);

                    if (updates.Count > 0)
                    {
                        _logger.LogInformation("{Count} package update(s) available.", updates.Count);
                        NotificationService.ShowInformation(
                            MessageStrings.PackageUpdatesAvailable,
                            string.Format(MessageStrings.PackagesCanBeUpdated, updates.Count),
                            onActionButtonClick: OpenExtensionsPage,
                            actionButtonText: Strings.Open);
                    }
                    else
                    {
                        _logger.LogInformation("All packages are up to date.");
                    }
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    _logger.LogError(ex, "An error occurred while checking for package updates.");
                }
            }
        });
    }

    public override Task Task { get; }

    private static async void OpenExtensionsPage()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (AppHelper.GetTopLevel() is not Window topLevel)
                    return;

                var pageExtension = ExtensionsPageExtension.Instance;
                var control = pageExtension.CreateControl();
                var dataContext = pageExtension.CreateContext();
                control.DataContext = dataContext;
                if (control is Pages.ExtensionsPage page)
                {
                    page.nav.SelectedItem = page.nav.MenuItemsSource.Cast<NavigationViewItem>().ElementAtOrDefault(1);
                }

                if (control is Window dialog)
                {
                    await dialog.ShowDialog(topLevel);
                }
                else
                {
                    var window = new Window { Content = control, Title = dataContext.Header };
                    await window.ShowDialog(topLevel);
                }
            });
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
    }
}
