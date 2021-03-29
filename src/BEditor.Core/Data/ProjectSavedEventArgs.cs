using System;

namespace BEditor.Data
{
    /// <summary>
    /// Provides data for the <see cref="Project.Saved"/> event.
    /// </summary>
    public class ProjectSavedEventArgs : EventArgs
    {
        /// <summary>
        /// <see cref="ProjectSavedEventArgs"/> Initialize a new instance of the class.
        /// </summary>
        public ProjectSavedEventArgs(SaveType type) => Type = type;

        /// <summary>
        /// Gets the save type.
        /// </summary>
        public SaveType Type { get; }
    }
}
