using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data;
using BEditor.Core.Data.Property.Easing;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Core.Service;

namespace BEditor.Core.Plugin
{
    /// <summary>
    /// Represents the class that manages the plugin.
    /// </summary>
    public class PluginManager
    {
        internal readonly List<IPlugin> _loaded = new();
        internal readonly List<(string, IEnumerable<ICustomMenu>)> _menus = new();
        /// <summary>
        /// Gets a default <see cref="PluginManager"/> instance.
        /// </summary>
        public static readonly PluginManager Default = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginManager"/> class.
        /// </summary>
        public PluginManager()
        {

        }

        /// <summary>
        /// Get the loaded plugins.
        /// </summary>
        public IEnumerable<IPlugin> Plugins => _loaded;
        /// <summary>
        /// Get or set the base directory from which to retrieve plugins.
        /// </summary>
        public string BaseDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "user", "plugins");

        /// <summary>
        /// Get all plugin names.
        /// </summary>
        /// <returns>All plugin names.</returns>
        public IEnumerable<string> GetNames()
        {
            return Directory.GetDirectories(BaseDirectory)
                .Select(static folder => Path.GetFileName(folder));
        }

        /// <summary>
        /// Load the assembly from the name of the plugin.
        /// </summary>
        /// <param name="pluginName">The name of the plugin to load.</param>
        /// <exception cref="PluginException">Plugin failded to load.</exception>
        public void Load(IEnumerable<string> pluginName)
        {
            var plugins = pluginName
                .Where(static f => f is not null)
                .Select(f => Path.Combine(BaseDirectory, f, $"{f}.dll"))
                .Where(static f => File.Exists(f))
                .Select(static f => Assembly.LoadFrom(f));

            var args = Environment.GetCommandLineArgs();
            foreach (var asm in plugins)
            {
                try
                {
                    asm.GetTypes().Where(t => t.Name is "Plugin")
                        .FirstOrDefault()
                        ?.InvokeMember("Register", BindingFlags.InvokeMethod, null, null, new object[] { args });
                }
                catch(Exception e)
                {
                    throw new PluginException(string.Format(Resources.FailedToLoad, asm.GetName().Name), e);
                }
            }
        }
    }
}
