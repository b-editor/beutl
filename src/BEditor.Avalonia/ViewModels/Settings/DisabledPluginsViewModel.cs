using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public class DisabledPluginsViewModel
    {
        public DisabledPluginsViewModel()
        {
            Enable.Where(_ => SelectName.Value is not null)
                .Subscribe(_ =>
            {
                BEditor.Settings.Default.EnablePlugins.Add(SelectName.Value);
                BEditor.Settings.Default.DisablePlugins.Remove(SelectName.Value);

                BEditor.Settings.Default.Save();
            });
            IsSelected = SelectName.Select(n => n is not null).ToReadOnlyReactivePropertySlim();
        }

        public ReactivePropertySlim<string> SelectName { get; } = new();
        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }
        public ReactiveCommand Enable { get; } = new();
    }
}