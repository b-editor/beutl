// ProjectSavedEventArgs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data
{
    /// <summary>
    /// Provides data for the <see cref="Project.Saved"/> event.
    /// </summary>
    public class ProjectSavedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProjectSavedEventArgs"/> class.
        /// </summary>
        /// <param name="type">The save type.</param>
        public ProjectSavedEventArgs(SaveType type)
        {
            Type = type;
        }

        /// <summary>
        /// Gets the save type.
        /// </summary>
        public SaveType Type { get; }
    }
}