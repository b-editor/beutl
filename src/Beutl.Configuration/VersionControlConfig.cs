using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class VersionControlConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool> EnableForNewProjectsProperty;
    public static readonly CoreProperty<bool> AutoCommitOnSaveProperty;
    public static readonly CoreProperty<bool> AutoCommitOnCloseProperty;
    public static readonly CoreProperty<string?> GitExecutablePathProperty;
    public static readonly CoreProperty<bool> UseLfsWhenAvailableProperty;
    public static readonly CoreProperty<int> LargeMediaWarningThresholdMbProperty;

    static VersionControlConfig()
    {
        EnableForNewProjectsProperty = ConfigureProperty<bool, VersionControlConfig>(nameof(EnableForNewProjects))
            .DefaultValue(true)
            .Register();

        AutoCommitOnSaveProperty = ConfigureProperty<bool, VersionControlConfig>(nameof(AutoCommitOnSave))
            .DefaultValue(true)
            .Register();

        AutoCommitOnCloseProperty = ConfigureProperty<bool, VersionControlConfig>(nameof(AutoCommitOnClose))
            .DefaultValue(true)
            .Register();

        GitExecutablePathProperty = ConfigureProperty<string?, VersionControlConfig>(nameof(GitExecutablePath))
            .DefaultValue(null)
            .Register();

        UseLfsWhenAvailableProperty = ConfigureProperty<bool, VersionControlConfig>(nameof(UseLfsWhenAvailable))
            .DefaultValue(true)
            .Register();

        LargeMediaWarningThresholdMbProperty
            = ConfigureProperty<int, VersionControlConfig>(nameof(LargeMediaWarningThresholdMb))
                .DefaultValue(50)
                .Register();
    }

    public bool EnableForNewProjects
    {
        get => GetValue(EnableForNewProjectsProperty);
        set => SetValue(EnableForNewProjectsProperty, value);
    }

    public bool AutoCommitOnSave
    {
        get => GetValue(AutoCommitOnSaveProperty);
        set => SetValue(AutoCommitOnSaveProperty, value);
    }

    public bool AutoCommitOnClose
    {
        get => GetValue(AutoCommitOnCloseProperty);
        set => SetValue(AutoCommitOnCloseProperty, value);
    }

    public string? GitExecutablePath
    {
        get => GetValue(GitExecutablePathProperty);
        set => SetValue(GitExecutablePathProperty, value);
    }

    public bool UseLfsWhenAvailable
    {
        get => GetValue(UseLfsWhenAvailableProperty);
        set => SetValue(UseLfsWhenAvailableProperty, value);
    }

    public int LargeMediaWarningThresholdMb
    {
        get => GetValue(LargeMediaWarningThresholdMbProperty);
        set => SetValue(LargeMediaWarningThresholdMbProperty, value);
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
