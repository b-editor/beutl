using Beutl.Configuration;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class PrivacySettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly TelemetryConfig _config;

    public PrivacySettingsPageViewModel(TelemetryConfig configuration)
    {
        _config = configuration;

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

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
