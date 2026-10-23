using System.ComponentModel;

namespace Beutl.Configuration;

public sealed partial class VersionControlConfig : ConfigurationBase
{
    public VersionControlConfig()
    {
        ScanProperties<VersionControlConfig>();
    }

    public IProperty<bool> EnableForNewProjects { get; } = Property.CreateValue(true);

    public IProperty<bool> AutoCommitOnSave { get; } = Property.CreateValue(true);

    public IProperty<bool> AutoCommitOnClose { get; } = Property.CreateValue(true);

    public IProperty<string?> GitExecutablePath { get; } = Property.CreateValue<string?>(null);

    public IProperty<bool> UseLfsWhenAvailable { get; } = Property.CreateValue(true);

    public IProperty<int> LargeMediaWarningThresholdMb { get; } = Property.CreateValue(50);

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
