// ApplyType.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Represents the type of rendering request.
    /// </summary>
    public enum ApplyType
    {
        /// <summary>
        /// Represents the preview rendering during editing.
        /// </summary>
        Edit,

        /// <summary>
        /// Represents the rendering in the image output.
        /// </summary>
        Image,

        /// <summary>
        /// Represents the rendering in the video output.
        /// </summary>
        Video,

        /// <summary>
        /// Represents the rendering in the audio output.
        /// </summary>
        Audio,
    }
}