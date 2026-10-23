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

        Beutl_Application = _config.Beutl_Application.GetObservable()
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_PackageManagement = _config.Beutl_PackageManagement.GetObservable()
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_Api_Client = _config.Beutl_Api_Client.GetObservable()
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_Logging = _config.Beutl_Logging.GetObservable()
            .Select(v => v == true)
            .ToReactiveProperty()
            .DisposeWith(_disposables);

        Beutl_Application.Subscribe(b => _config.Beutl_Application.Value = b);
        Beutl_PackageManagement.Subscribe(b => _config.Beutl_PackageManagement.Value = b);
        Beutl_Api_Client.Subscribe(b => _config.Beutl_Api_Client.Value = b);
        Beutl_Logging.Subscribe(b =>
        {
            _config.Beutl_Logging.Value = b;

            if (b)
            {
                _config.Beutl_Application.Value = true;
                _config.Beutl_PackageManagement.Value = true;
                _config.Beutl_Api_Client.Value = true;
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
