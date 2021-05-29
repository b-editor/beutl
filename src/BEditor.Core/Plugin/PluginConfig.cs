// PluginConfig.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Plugin
{
    /// <summary>
    /// Represents a plugin config.
    /// </summary>
    public class PluginConfig
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PluginConfig"/> class.
        /// </summary>
        /// <param name="app">The application.</param>
        public PluginConfig(IApplication app)
        {
            Application = app;
        }

        /// <summary>
        /// Gets the application.
        /// </summary>
        public IApplication Application { get; }
    }
}