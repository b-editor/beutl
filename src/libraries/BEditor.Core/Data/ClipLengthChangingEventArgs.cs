// ClipLengthChangingEventArgs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Provides data for the <see cref="ClipElement.LengthChanging"/> event.
    /// </summary>
    public sealed class ClipLengthChangingEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipLengthChangingEventArgs"/> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="newLength">The new length.</param>
        /// <param name="oldLength">The old length.</param>
        public ClipLengthChangingEventArgs(ClipLengthChangeAnchor anchor, Frame newLength, Frame oldLength)
        {
            Anchor = anchor;
            NewLength = newLength;
            OldLength = oldLength;
        }

        /// <summary>
        /// Gets or sets the anchor.
        /// </summary>
        public ClipLengthChangeAnchor Anchor { get; set; }

        /// <summary>
        /// Gets or sets the new length.
        /// </summary>
        public Frame NewLength { get; set; }

        /// <summary>
        /// Gets the old length.
        /// </summary>
        public Frame OldLength { get; }
    }
}