using System.Collections.ObjectModel;

using BEditor.Properties;

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

        public string Message => string.Format(Strings.PluginsAddedMessage, Plugins.Count.ToString());
    }
}