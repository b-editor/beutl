using Beutl.Configuration;
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

        Beutl_Application = _config.GetObservable(TelemetryConfig.Beutl_ApplicationProperty)
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_PackageManagement = _config.GetObservable(TelemetryConfig.Beutl_PackageManagementProperty)
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_Api_Client = _config.GetObservable(TelemetryConfig.Beutl_Api_ClientProperty)
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_Logging = _config.GetObservable(TelemetryConfig.Beutl_LoggingProperty)
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_Application.Subscribe(b => _config.Beutl_Application = b);
        Beutl_PackageManagement.Subscribe(b => _config.Beutl_PackageManagement = b);
        Beutl_Api_Client.Subscribe(b => _config.Beutl_Api_Client = b);
        Beutl_Logging.Subscribe(b =>
        {
            _config.Beutl_Logging = b;

            if (b)
            {
                _config.Beutl_Application = true;
                _config.Beutl_PackageManagement = true;
                _config.Beutl_Api_Client = true;
            }
        });
    }

    public ReactiveProperty<bool> Beutl_Application { get; }

    public ReactiveProperty<bool> Beutl_PackageManagement { get; }

    public ReactiveProperty<bool> Beutl_Api_Client { get; }
    
    public ReactiveProperty<bool> Beutl_Logging { get; }

    public override void Dispose()
    {
        _disposables.Dispose();
    }
}
