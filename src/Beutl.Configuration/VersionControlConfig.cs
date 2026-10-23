using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class VersionControlConfig : ConfigurationBase
{
    public VersionControlConfig()
    {
        ScanProperties<VersionControlConfig>();
    }

    public IProperty<bool> EnableForNewProjects { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> AutoCommitOnSave { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> AutoCommitOnClose { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<string?> GitExecutablePath { get; } = new ValueOnlyProperty<string?>(null);

    public IProperty<bool> UseLfsWhenAvailable { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<int> LargeMediaWarningThresholdMb { get; } = new ValueOnlyProperty<int>(50);

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is not (nameof(Id) or nameof(Name)))
        {
            OnChanged();
        }
    }
}
