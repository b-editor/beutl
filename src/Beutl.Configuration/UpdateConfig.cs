using System.ComponentModel;

namespace Beutl.Configuration;

public sealed partial class UpdateConfig : ConfigurationBase
{
    public UpdateConfig()
    {
        ScanProperties<UpdateConfig>();
    }

    // Whether an update found at startup downloads in the background without being asked. Installing it still waits
    // for the user.
    public IProperty<bool> AutoDownload { get; } = Property.CreateValue(true);

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
