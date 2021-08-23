// ClipLengthChangeAnchor.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data
{
    /// <summary>
    /// Types of anchors used in <see cref="ClipLengthChangedEventArgs"/>.
    /// </summary>
    public enum ClipLengthChangeAnchor
    {
        /// <summary>
        /// The starting frame.
        /// </summary>
        Start,

        /// <summary>
        /// The ending frame.
        /// </summary>
        End,
    }
}