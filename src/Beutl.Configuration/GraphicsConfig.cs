using System.ComponentModel;

namespace Beutl.Configuration;

public sealed partial class GraphicsConfig : ConfigurationBase
{
    public GraphicsConfig()
    {
        ScanProperties<GraphicsConfig>();
    }

    public IProperty<string?> SelectedGpuName { get; } = Property.CreateValue<string?>(null);

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
