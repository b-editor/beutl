using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Models.ManagePlugins;
using BEditor.Plugin;

namespace BEditor.Models.ManagePlugins
{
    public class PluginChangeSchedule
    {
        public static ObservableCollection<PluginUpdateOrInstall> UpdateOrInstall { get; } = new();

        public static ObservableCollection<PluginObject> Uninstall { get; } = new();
    }
}
