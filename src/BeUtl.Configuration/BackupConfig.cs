using System.ComponentModel;

namespace BeUtl.Configuration;

public sealed class BackupConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool> BackupSettingsProperty;

    static BackupConfig()
    {
        BackupSettingsProperty = ConfigureProperty<bool, BackupConfig>(nameof(BackupSettings))
            .SerializeName("backup-settings")
            .DefaultValue(true)
            .PropertyFlags(PropertyFlags.NotifyChanged)
            .Register();
    }

    public bool BackupSettings
    {
        get => GetValue(BackupSettingsProperty);
        set => SetValue(BackupSettingsProperty, value);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(BackupSettings))
        {
            OnChanged();
        }
    }
}
