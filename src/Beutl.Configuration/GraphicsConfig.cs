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
}
