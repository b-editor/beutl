using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class TelemetryConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool?> Beutl_LoggingProperty;
    public static readonly CoreProperty<bool?> Beutl_ApplicationProperty;
    public static readonly CoreProperty<bool?> Beutl_PackageManagementProperty;
    public static readonly CoreProperty<bool?> Beutl_Api_ClientProperty;

    static TelemetryConfig()
    {
        Beutl_LoggingProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Logging))
            .DefaultValue(null)
            .Register();
        
        Beutl_ApplicationProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Application))
            .DefaultValue(null)
            .Register();

        Beutl_PackageManagementProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_PackageManagement))
            .DefaultValue(null)
            .Register();

        Beutl_Api_ClientProperty = ConfigureProperty<bool?, TelemetryConfig>(nameof(Beutl_Api_Client))
            .DefaultValue(null)
            .Register();
    }

    public bool? Beutl_Logging
    {
        get => GetValue(Beutl_LoggingProperty);
        set => SetValue(Beutl_LoggingProperty, value);
    }
    
    public bool? Beutl_Application
    {
        get => GetValue(Beutl_ApplicationProperty);
        set => SetValue(Beutl_ApplicationProperty, value);
    }

    public bool? Beutl_PackageManagement
    {
        get => GetValue(Beutl_PackageManagementProperty);
        set => SetValue(Beutl_PackageManagementProperty, value);
    }

    public bool? Beutl_Api_Client
    {
        get => GetValue(Beutl_Api_ClientProperty);
        set => SetValue(Beutl_Api_ClientProperty, value);
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
