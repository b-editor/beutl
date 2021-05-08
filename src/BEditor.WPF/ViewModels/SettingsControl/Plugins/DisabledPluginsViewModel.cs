using System;
using System.Linq;
using System.Reactive.Linq;

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

        public ReactivePropertySlim<string> SelectName { get; } = new();
        public ReactiveCommand Enable { get; } = new();
    }
}