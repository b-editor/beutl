using System.ComponentModel;

namespace Beutl.Configuration;

public sealed partial class TelemetryConfig : ConfigurationBase
{
    public TelemetryConfig()
    {
        ScanProperties<TelemetryConfig>();
    }

    public IProperty<bool?> Beutl_Logging { get; } = Property.CreateValue<bool?>(null);

    public IProperty<bool?> Beutl_Application { get; } = Property.CreateValue<bool?>(null);

    public IProperty<bool?> Beutl_PackageManagement { get; } = Property.CreateValue<bool?>(null);

    public IProperty<bool?> Beutl_Api_Client { get; } = Property.CreateValue<bool?>(null);

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
