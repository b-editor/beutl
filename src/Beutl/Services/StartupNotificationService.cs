using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Beutl.Configuration;
using Beutl.Logging;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

internal static class StartupNotificationService
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(StartupNotificationService));

    internal const string TelemetryDetailsUrl = "https://beutl.beditor.net/about/telemetry";
    internal const int MaxVisibleSideloadPackages = 3;
    internal const int MaxSideloadPackageNameLength = 80;

    public static void ShowTelemetryConsent(TelemetryConfig config)
    {
        if (Telemetry.IsConsentConfigured(config))
        {
            return;
        }

        NotificationService.ShowInformation(
            SettingsStrings.Telemetry,
            SettingsStrings.Telemetry_Description,
            expiration: Timeout.InfiniteTimeSpan,
            actions:
            [
                new(Strings.ShowDetails, OpenTelemetryDetails, DismissOnInvoke: false),
                new(Strings.Disagree, () => SetTelemetryEnabled(config, false)),
                new(Strings.Agree, () => SetTelemetryEnabled(config, true))
            ],
            isClosable: false);
    }

    public static Task<bool> ConfirmSideloadExtensions(IReadOnlyList<string> packageNames)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        string[] packageSnapshot = packageNames.ToArray();
        int detailsDialogOpen = 0;

        async Task ShowDetailsAsync()
        {
            if (Interlocked.CompareExchange(ref detailsDialogOpen, 1, 0) != 0)
                return;

            try
            {
                await CreateSideloadDetailsDialog(packageSnapshot).ShowAsync();
            }
            catch (Exception e)
            {
                s_logger.LogError(e, "Failed to show sideload package details.");
            }
            finally
            {
                Volatile.Write(ref detailsDialogOpen, 0);
            }
        }

        var actions = new List<NotificationAction>();
        if (RequiresSideloadDetails(packageSnapshot))
        {
            actions.Add(new(
                Strings.ShowDetails,
                () => _ = ShowDetailsAsync(),
                DismissOnInvoke: false));
        }

        actions.Add(new(Strings.Yes, () => completion.TrySetResult(true)));

        NotificationService.ShowWarning(
            MessageStrings.ConfirmLoadSideloadExtensions,
            FormatSideloadPackageNames(packageSnapshot),
            expiration: Timeout.InfiniteTimeSpan,
            onClose: () => completion.TrySetResult(false),
            actions: actions,
            onShowFailed: () => completion.TrySetResult(false));

        return completion.Task;
    }

    internal static ContentDialog CreateSideloadDetailsDialog(IReadOnlyList<string> packageNames)
    {
        return new ContentDialog
        {
            Title = MessageStrings.ConfirmLoadSideloadExtensions,
            Content = new ListBox
            {
                ItemsSource = packageNames.ToArray(),
                SelectedIndex = packageNames.Count > 0 ? 0 : -1,
                MinWidth = 320,
                MaxWidth = 520,
                MaxHeight = 400,
                ItemTemplate = new FuncDataTemplate<string>((name, _) => new TextBlock
                {
                    Text = name,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 480
                })
            },
            CloseButtonText = Strings.Close,
            DefaultButton = ContentDialogButton.Close
        };
    }

    private static bool RequiresSideloadDetails(IReadOnlyList<string> packageNames)
    {
        return packageNames.Count > MaxVisibleSideloadPackages
            || packageNames.Any(name => !string.Equals(
                name,
                FormatSideloadPackageName(name),
                StringComparison.Ordinal));
    }

    private static string FormatSideloadPackageNames(IReadOnlyList<string> packageNames)
    {
        IEnumerable<string> visibleNames = packageNames
            .Take(MaxVisibleSideloadPackages)
            .Select(FormatSideloadPackageName);

        if (packageNames.Count > MaxVisibleSideloadPackages)
        {
            visibleNames = visibleNames.Append(string.Format(
                MessageStrings.AndMorePackages,
                packageNames.Count - MaxVisibleSideloadPackages));
        }

        return string.Join(Environment.NewLine, visibleNames);
    }

    private static string FormatSideloadPackageName(string packageName)
    {
        string singleLineName = packageName
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

        return singleLineName.Length <= MaxSideloadPackageNameLength
            ? singleLineName
            : $"{singleLineName[..(MaxSideloadPackageNameLength - 1)]}…";
    }

    private static void OpenTelemetryDetails()
    {
        Process.Start(new ProcessStartInfo(TelemetryDetailsUrl)
        {
            UseShellExecute = true,
            Verb = "open"
        });
    }

    private static void SetTelemetryEnabled(TelemetryConfig config, bool value)
    {
        config.Beutl_Api_Client = value;
        config.Beutl_Application = value;
        config.Beutl_PackageManagement = value;
        config.Beutl_Logging = value;
    }
}
