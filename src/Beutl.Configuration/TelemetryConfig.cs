using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class TelemetryConfig : ConfigurationBase
{
    public TelemetryConfig()
    {
        ScanProperties<TelemetryConfig>();
    }

    public IProperty<bool?> Beutl_LoggingProperty { get; } = new ValueOnlyProperty<bool?>(null);

    public IProperty<bool?> Beutl_ApplicationProperty { get; } = new ValueOnlyProperty<bool?>(null);

    public IProperty<bool?> Beutl_PackageManagementProperty { get; } = new ValueOnlyProperty<bool?>(null);

    public IProperty<bool?> Beutl_Api_ClientProperty { get; } = new ValueOnlyProperty<bool?>(null);

    public bool? Beutl_Logging
    {
        get => Beutl_LoggingProperty.Value;
        set => Beutl_LoggingProperty.Value = value;
    }

    public bool? Beutl_Application
    {
        get => Beutl_ApplicationProperty.Value;
        set => Beutl_ApplicationProperty.Value = value;
    }

    public bool? Beutl_PackageManagement
    {
        get => Beutl_PackageManagementProperty.Value;
        set => Beutl_PackageManagementProperty.Value = value;
    }

    public bool? Beutl_Api_Client
    {
        get => Beutl_Api_ClientProperty.Value;
        set => Beutl_Api_ClientProperty.Value = value;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
