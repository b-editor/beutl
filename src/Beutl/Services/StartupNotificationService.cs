using Beutl.Configuration;

namespace Beutl.Services;

internal static class StartupNotificationService
{
    internal const string TelemetryDetailsUrl = "https://beutl.beditor.net/about/telemetry";

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
            $"{SettingsStrings.Telemetry_Description}{Environment.NewLine}{TelemetryDetailsUrl}",
            expiration: Timeout.InfiniteTimeSpan,
            onClose: () => SetTelemetryEnabled(config, false),
            onActionButtonClick: () => SetTelemetryEnabled(config, true),
            actionButtonText: Strings.Agree);
    }

    public static Task<bool> ConfirmSideloadExtensions(IReadOnlyList<string> packageNames)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        NotificationService.ShowWarning(
            MessageStrings.ConfirmLoadSideloadExtensions,
            string.Join(Environment.NewLine, packageNames),
            expiration: Timeout.InfiniteTimeSpan,
            onClose: () => completion.TrySetResult(false),
            onActionButtonClick: () => completion.TrySetResult(true),
            actionButtonText: Strings.Yes);

        return completion.Task;
    }

    private static void SetTelemetryEnabled(TelemetryConfig config, bool value)
    {
        config.Beutl_Api_Client = value;
        config.Beutl_Application = value;
        config.Beutl_PackageManagement = value;
        config.Beutl_Logging = value;
    }
}
