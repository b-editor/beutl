// IApplication.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Threading;

using BEditor.Audio;
using BEditor.Data;

namespace BEditor
{
    /// <summary>
    /// Represents an application.
    /// </summary>
    public interface IApplication : IParentSingle<Project?>, ITopLevel
    {
        /// <summary>
        /// Occurs when the project is opened.
        /// </summary>
        public event EventHandler<ProjectOpenedEventArgs>? ProjectOpened;

        /// <summary>
        /// Occurs when the application is exiting.
        /// </summary>
        public event EventHandler? Exit;

        /// <summary>
        /// Gets or sets the status of an application.
        /// </summary>
        public Status AppStatus { get; set; }

        /// <summary>
        /// Gets audio context.
        /// </summary>
        public AudioContext? AudioContext { get; }

        /// <summary>
        /// Restore the application configuration.
        /// </summary>
        /// <param name="project">The project to restore the config.</param>
        /// <param name="directory">The directory of config.</param>
        public void RestoreAppConfig(Project project, string directory);

        /// <summary>
        /// Save the application configuration.
        /// </summary>
        /// <param name="project">The project to save the config.</param>
        /// <param name="directory">The directory of config.</param>
        public void SaveAppConfig(Project project, string directory);
    }
}