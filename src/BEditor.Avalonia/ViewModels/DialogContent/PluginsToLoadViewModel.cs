using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.DialogContent
{
    public class PluginsToLoadViewModel
    {
        private ObservableCollection<PluginToLoad>? _plugins;

        public ObservableCollection<PluginToLoad> Plugins
        {
            get => _plugins ??= new();
            set => _plugins = value;
        }

        public string Message => string.Format(Strings.PluginsAddedMessage, Plugins.Count.ToString());
    }

    public class PluginToLoad
    {
        public ReactivePropertySlim<string> Name { get; } = new();
        public ReactivePropertySlim<bool> IsEnabled { get; } = new();
    }
}
