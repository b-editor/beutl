using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.ViewModels.SettingsControl.Plugins
{
    public class DisabledPluginsViewModel
    {
        public DisabledPluginsViewModel()
        {
            Enable.Where(_ => SelectName.Value is not null)
                .Subscribe(_ =>
            {
                Settings.Default.EnablePlugins.Add(SelectName.Value);
                Settings.Default.DisablePlugins.Remove(SelectName.Value);

                Settings.Default.Save();
            });
        }

        public ReactiveProperty<string> SelectName { get; } = new();
        public ReactiveCommand Enable { get; } = new();
    }
}
