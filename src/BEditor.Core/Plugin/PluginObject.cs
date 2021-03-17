
using System;
using System.Collections.Generic;
using System.Threading;

using BEditor.Data;
using BEditor.Data.Property.Easing;

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents a plug-in that extends the functionality of the application.
    /// </summary>
    public abstract class PluginObject
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginObject"/> class.
        /// </summary>
        /// <param name="config">The plugin config.</param>
        protected PluginObject(PluginConfig config)
        {
            App = config.Application;
        }

        /// <summary>
        /// Get the name of the plugin.
        /// </summary>
        public abstract string PluginName { get; }

        /// <summary>
        /// Get the description of the plugin.
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Gets the Application.
        /// </summary>
        public IApplication App { get; }

        /// <summary>
        /// Get the name of the assembly for this plugin.
        /// </summary>
        public string AssemblyName => GetType().Assembly.GetName().Name!;

        /// <summary>
        /// Get or set the settings for this plugin.
        /// </summary>
        public abstract SettingRecord Settings { get; set; }
    }
}
