// ClipLengthChangedEventArgs.cs
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
    /// Provides data for the <see cref="ClipElement.LengthChanged"/> event.
    /// </summary>
    public sealed class ClipLengthChangedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipLengthChangedEventArgs"/> class.
        /// </summary>
        /// <param name="anchor">The anchor.</param>
        /// <param name="newLength">The new length.</param>
        /// <param name="oldLength">The old length.</param>
        public ClipLengthChangedEventArgs(ClipLengthChangeAnchor anchor, Frame newLength, Frame oldLength)
        {
            Anchor = anchor;
            NewLength = newLength;
            OldLength = oldLength;
        }

        /// <summary>
        /// Gets the anchor.
        /// </summary>
        public ClipLengthChangeAnchor Anchor { get; }

        /// <summary>
        /// Gets the new length.
        /// </summary>
        public Frame NewLength { get; }

        /// <summary>
        /// Gets the old length.
        /// </summary>
        public Frame OldLength { get; }
    }
}