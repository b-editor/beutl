// Status.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor
{
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
        Output,
    }
}