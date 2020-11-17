using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Plugin;

namespace BEditor.Core.Data
{
    public interface IApplication
    {
        public Status AppStatus { get; set; }
        public List<IPlugin> LoadedPlugins { get; }
    }
}
