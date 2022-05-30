using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Configuration;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class BackupSettingsPageViewModel
{
    private readonly BackupConfig _config;

    public BackupSettingsPageViewModel()
    {
        _config = GlobalConfiguration.Instance.BackupConfig;
        BackupSettings = _config.GetObservable(BackupConfig.BackupSettingsProperty).ToReactiveProperty();
        BackupSettings.Subscribe(b => _config.BackupSettings = b);
    }

    public ReactiveProperty<bool> BackupSettings { get; }
}
