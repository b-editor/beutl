using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class VersionControlConfig : ConfigurationBase
{
    public VersionControlConfig()
    {
        ScanProperties<VersionControlConfig>();
    }

    public IProperty<bool> EnableForNewProjectsProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> AutoCommitOnSaveProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<bool> AutoCommitOnCloseProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<string?> GitExecutablePathProperty { get; } = new ValueOnlyProperty<string?>(null);

    public IProperty<bool> UseLfsWhenAvailableProperty { get; } = new ValueOnlyProperty<bool>(true);

    public IProperty<int> LargeMediaWarningThresholdMbProperty { get; } = new ValueOnlyProperty<int>(50);

    public bool EnableForNewProjects
    {
        get => EnableForNewProjectsProperty.Value;
        set => EnableForNewProjectsProperty.Value = value;
    }

    public bool AutoCommitOnSave
    {
        get => AutoCommitOnSaveProperty.Value;
        set => AutoCommitOnSaveProperty.Value = value;
    }

    public bool AutoCommitOnClose
    {
        get => AutoCommitOnCloseProperty.Value;
        set => AutoCommitOnCloseProperty.Value = value;
    }

    public string? GitExecutablePath
    {
        get => GitExecutablePathProperty.Value;
        set => GitExecutablePathProperty.Value = value;
    }

    public bool UseLfsWhenAvailable
    {
        get => UseLfsWhenAvailableProperty.Value;
        set => UseLfsWhenAvailableProperty.Value = value;
    }

    public int LargeMediaWarningThresholdMb
    {
        get => LargeMediaWarningThresholdMbProperty.Value;
        set => LargeMediaWarningThresholdMbProperty.Value = value;
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
