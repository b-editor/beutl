using System.ComponentModel;

namespace Beutl.Configuration;

public sealed class BackupConfig : ConfigurationBase
{
    public static readonly CoreProperty<bool> BackupSettingsProperty;

    static BackupConfig()
    {
        BackupSettingsProperty = ConfigureProperty<bool, BackupConfig>(nameof(BackupSettings))
            .DefaultValue(true)
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
