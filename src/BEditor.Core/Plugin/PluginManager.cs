using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using System.Threading.Tasks;

using BEditor.Resources;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents the class that manages the plugin.
    /// </summary>
    public class PluginManager
    {
        /// <summary>
        /// Gets a default <see cref="PluginManager"/> instance.
        /// </summary>
        public static readonly PluginManager Default = new();

        /// <summary>
        /// The loaded plugins.
        /// </summary>
        internal readonly List<PluginObject> _loaded = new();

        /// <summary>
        /// The plugin menus.
        /// </summary>
        internal readonly List<(string, IEnumerable<ICustomMenu>)> _menus = new();

        internal readonly List<(PluginObject, List<Func<IProgress<int>, ValueTask>>)> _tasks = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginManager"/> class.
        /// </summary>
        public PluginManager()
        {
        }

        /// <summary>
        /// Gets the loaded plugins.
        /// </summary>
        public IEnumerable<PluginObject> Plugins => _loaded;

        /// <summary>
        /// Gets or sets the base directory from which to retrieve plugins.
        /// </summary>
        public string BaseDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "user", "plugins");

        /// <summary>
        /// Gets all plugin names.
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

            foreach (var asm in plugins)
            {
                try
                {
                    Array.Find(asm.GetTypes(), t => t.Name is "Plugin")
                        ?.InvokeMember("Register", BindingFlags.InvokeMethod, null, null, Array.Empty<object>());
                }
                catch (Exception e)
                {
                    throw new PluginException(string.Format(Strings.FailedToLoad, asm.GetName().Name), e);
                }
            }
        }
    }
}