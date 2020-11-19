using System;
using System.Collections.Generic;

using BEditor.Core.Plugin;
using BEditor.Core.Service;

namespace BEditor.Core.Data
{
    public interface IApplication
    {
        public Status AppStatus { get; set; }
        public List<IPlugin> LoadedPlugins { get; }
    }
}
