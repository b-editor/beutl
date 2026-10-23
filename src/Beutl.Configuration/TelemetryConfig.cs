using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class TelemetryConfig : ConfigurationBase
{
    public TelemetryConfig()
    {
        ScanProperties<TelemetryConfig>();
    }

    public IProperty<bool?> Beutl_Logging { get; } = new ValueOnlyProperty<bool?>(null);

    public IProperty<bool?> Beutl_Application { get; } = new ValueOnlyProperty<bool?>(null);

    public IProperty<bool?> Beutl_PackageManagement { get; } = new ValueOnlyProperty<bool?>(null);

    public IProperty<bool?> Beutl_Api_Client { get; } = new ValueOnlyProperty<bool?>(null);

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
