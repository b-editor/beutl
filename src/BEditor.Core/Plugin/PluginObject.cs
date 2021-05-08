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
        /// Gets the name of the plugin.
        /// </summary>
        public abstract string PluginName { get; }

        /// <summary>
        /// Gets the description of the plugin.
        /// </summary>
        public abstract string Description { get; }

        /// <summary>
        /// Gets the Application.
        /// </summary>
        public IApplication App { get; }

        /// <summary>
        /// Gets the name of the assembly for this plugin.
        /// </summary>
        public string AssemblyName => GetType().Assembly.GetName().Name!;

        /// <summary>
        /// Gets or sets the settings for this plugin.
        /// </summary>
        public abstract SettingRecord Settings { get; set; }
    }
}