
using System;
using System.Collections.Generic;

using BEditor.Core.Data;
using BEditor.Core.Data.Property.Easing;

namespace BEditor.Core.Plugin
{
    /// <summary>
    /// Represents a plug-in that extends the functionality of the application.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>
        /// Get the name of the plugin.
        /// </summary>
        public string PluginName { get; }

        /// <summary>
        /// Get the description of the plugin.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Get the name of the assembly for this plugin.
        /// </summary>
        public sealed string AssemblyName => GetType().Assembly.GetName().Name!;

        /// <summary>
        /// Get or set the settings for this plugin.
        /// </summary>
        public SettingRecord Settings { get; set; }
    }
}
