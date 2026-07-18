using Beutl.Configuration;

namespace Beutl.Services;

internal static class StartupNotificationService
{
    internal const string TelemetryDetailsUrl = "https://beutl.beditor.net/about/telemetry";
    internal const int MaxVisibleSideloadPackages = 3;
    internal const int MaxSideloadPackageNameLength = 80;

    public static void ShowTelemetryConsent(TelemetryConfig config)
    {
        if (config.Beutl_Api_Client.HasValue
            && config.Beutl_Application.HasValue
            && config.Beutl_PackageManagement.HasValue
            && config.Beutl_Logging.HasValue)
        {
            return;
        }

        NotificationService.ShowInformation(
            SettingsStrings.Telemetry,
            SettingsStrings.Telemetry_Description,
            expiration: Timeout.InfiniteTimeSpan,
            onClose: () => SetTelemetryEnabled(config, false),
            actions:
            [
                new(Strings.ShowDetails, OpenTelemetryDetails, DismissOnInvoke: false),
                new(Strings.Agree, () => SetTelemetryEnabled(config, true))
            ]);
    }

    public static Task<bool> ConfirmSideloadExtensions(IReadOnlyList<string> packageNames)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        NotificationService.ShowWarning(
            MessageStrings.ConfirmLoadSideloadExtensions,
            FormatSideloadPackageNames(packageNames),
            expiration: Timeout.InfiniteTimeSpan,
            onClose: () => completion.TrySetResult(false),
            actions: [new(Strings.Yes, () => completion.TrySetResult(true))]);

        return completion.Task;
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
