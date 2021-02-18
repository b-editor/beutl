using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.MessageContent
{
    public class PluginCheckHostViewModel
    {
        private ObservableCollection<PluginCheckViewModel>? plugins;

        public ObservableCollection<PluginCheckViewModel> Plugins
        {
            get => plugins ??= new();
            set => plugins = value;
        }

        public string Message => string.Format(Resources.PluginsAddedMessage, Plugins.Count.ToString());
    }
}
