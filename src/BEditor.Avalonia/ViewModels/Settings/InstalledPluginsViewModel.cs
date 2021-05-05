using System;
using System.Linq;
using System.Reactive.Linq;

using BEditor.Plugin;

using Reactive.Bindings;

namespace BEditor.ViewModels.Settings
{
    public sealed class LoadedPluginsViewModel
    {
        public LoadedPluginsViewModel()
        {
            UnloadClick.Where(_ => SelectPlugin is not null)
                .Subscribe(_ =>
                {
                    var name = SelectPlugin.Value.AssemblyName;

                    BEditor.Settings.Default.EnablePlugins.Remove(name);

                    if (!BEditor.Settings.Default.DisablePlugins.Contains(name))
                    {
                        BEditor.Settings.Default.DisablePlugins.Add(name);
                    }

                    BEditor.Settings.Default.Save();
                });

            IsSelected = SelectPlugin.Select(plugin => plugin is not null).ToReadOnlyReactivePropertySlim();
        }

        public ReactiveProperty<PluginObject> SelectPlugin { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReactiveCommand UnloadClick { get; } = new();
    }
}