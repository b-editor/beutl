// ProjectOpenedEventArgs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Data;

namespace BEditor
{
    /// <summary>
    /// Provides data for the <see cref="IApplication.ProjectOpened"/> event.
    /// </summary>
    public class ProjectOpenedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectOpenedEventArgs"/> class.
        /// </summary>
        /// <param name="project">The opened project.</param>
        public ProjectOpenedEventArgs(Project project)
        {
            Project = project;
        }

        /// <summary>
        /// Gets the <see cref="Project"/>.
        /// </summary>
        public Project Project { get; }
    }
}