using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class GraphicsConfig : ConfigurationBase
{
    public static readonly CoreProperty<string?> SelectedGpuNameProperty;

    static GraphicsConfig()
    {
        SelectedGpuNameProperty = ConfigureProperty<string?, GraphicsConfig>(nameof(SelectedGpuName))
            .DefaultValue(null)
            .Register();
    }

    public GraphicsConfig()
    {
    }

    public string? SelectedGpuName
    {
        get => GetValue(SelectedGpuNameProperty);
        set => SetValue(SelectedGpuNameProperty, value);
    }

    // Read once at startup, so a change has to reach settings.json before Beutl next starts.
    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
