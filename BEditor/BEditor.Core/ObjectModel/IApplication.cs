using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.ObjectModel.ProjectData;
using BEditor.Core.Plugin;

namespace BEditor.ObjectModel
{
    public interface IApplication
    {
        public Status AppStatus { get; set; }
        public Project Project { get; set; }
        public List<IPlugin> LoadedPlugins { get; }
        public string Path { get; }
    }
}
