using System;
using System.Reflection;

namespace BEditor.Core.Service
{
    /// <summary>
    /// Represents a class that provides UI-dependent services.
    /// </summary>
    public static class Services
    {
        /// <summary>
        /// Gets or sets the service that shows the file dialog.
        /// </summary>
        public static IFileDialogService? FileDialogService { get; set; }
    }

    /// <summary>
    /// Represents the status of the application.
    /// </summary>
    public enum Status
    {
        /// <summary>
        /// Represents the state of doing nothing.
        /// </summary>
        Idle,
        /// <summary>
        /// Represents the state of editing.
        /// </summary>
        Edit,
        /// <summary>
        /// Represents that the file has just been saved.
        /// </summary>
        Saved,
        /// <summary>
        /// Represents that the media player is playing.
        /// </summary>
        Playing,
        /// <summary>
        /// Represents that the output is in progress.
        /// </summary>
        Output
    }
}
