using System;
using System.Collections.Generic;

using BEditor.Core.Plugin;
using BEditor.Core.Service;

namespace BEditor.Core
{
    public interface IApplication
    {
        public Status AppStatus { get; set; }
        public List<IPlugin> LoadedPlugins { get; }
    }

    public class Application : IApplication
    {
        private Application()
        {

        }

        public static IApplication Empty() => new Application();

        Status IApplication.AppStatus { get; set; }
        List<IPlugin> IApplication.LoadedPlugins { get; }
    }
}
