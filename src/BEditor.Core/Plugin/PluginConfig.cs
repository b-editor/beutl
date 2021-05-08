namespace BEditor.Plugin
{
    /// <summary>
    /// Represents a plugin config.
    /// </summary>
    public class PluginConfig
    {
        /// <summary>
        /// Iniitializes a new instance of the <see cref="PluginConfig"/> class.
        /// </summary>
        public PluginConfig(IApplication app)
        {
            Application = app;
        }

        /// <summary>
        /// Gets the ServiceProvider.
        /// </summary>
        public IApplication Application { get; }
    }
}