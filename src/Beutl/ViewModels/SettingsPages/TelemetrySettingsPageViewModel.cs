using Beutl.Configuration;
using Beutl.Language;
using Beutl.Services;
using Beutl.ViewModels.ExtensionsPages;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class TelemetrySettingsPageViewModel : BasePageViewModel
{
    private readonly CompositeDisposable _disposables = [];
    private readonly TelemetryConfig _config;

    public TelemetrySettingsPageViewModel()
    {
        _config = GlobalConfiguration.Instance.TelemetryConfig;

        Beutl_Application = CreateProperty(TelemetryConfig.Beutl_ApplicationProperty);
        Beutl_PackageManagement = CreateProperty(TelemetryConfig.Beutl_PackageManagementProperty);
        Beutl_Api_Client = CreateProperty(TelemetryConfig.Beutl_Api_ClientProperty);
        Beutl_Logging = CreateProperty(TelemetryConfig.Beutl_LoggingProperty);
        UsageAnalytics = CreateProperty(TelemetryConfig.UsageAnalyticsProperty);

        Beutl_Application.Skip(1).Subscribe(value => _config.Beutl_Application = value).DisposeWith(_disposables);
        Beutl_PackageManagement.Skip(1).Subscribe(value => _config.Beutl_PackageManagement = value).DisposeWith(_disposables);
        Beutl_Api_Client.Skip(1).Subscribe(value => _config.Beutl_Api_Client = value).DisposeWith(_disposables);
        Beutl_Logging.Skip(1).Subscribe(value => _config.Beutl_Logging = value).DisposeWith(_disposables);
        UsageAnalytics.Skip(1).Subscribe(value => _config.UsageAnalytics = value).DisposeWith(_disposables);

        // Reset remains available after revocation so a user can remove a stale
        // persisted installation ID without turning analytics back on.
        ResetUsageIdentity = new ReactiveCommand();
        ResetUsageIdentity.Subscribe(ShowIdentityResetConfirmation).DisposeWith(_disposables);
    }

    public ReactiveProperty<bool> Beutl_Application { get; }

    public ReactiveProperty<bool> Beutl_PackageManagement { get; }

    public ReactiveProperty<bool> Beutl_Api_Client { get; }

    public ReactiveProperty<bool> Beutl_Logging { get; }

    public ReactiveProperty<bool> UsageAnalytics { get; }

    public ReactiveCommand ResetUsageIdentity { get; }

    public override void Dispose()
    {
        _disposables.Dispose();
    }

    private ReactiveProperty<bool> CreateProperty(CoreProperty<bool?> property)
    {
        return _config.GetObservable(property)
            .Select(value => value == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);
    }

    private static void ShowIdentityResetConfirmation()
    {
        NotificationService.ShowWarning(
            SettingsStrings.Telemetry_ResetUsageIdentity,
            SettingsStrings.Telemetry_ResetUsageIdentity_Description,
            expiration: Timeout.InfiniteTimeSpan,
            actions: [new(Strings.Yes, Telemetry.ResetUsageIdentity)]);
    }
}
