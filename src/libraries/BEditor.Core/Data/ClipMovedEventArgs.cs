// ClipMovedEventArgs.cs
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
    /// Provides data for the <see cref="ClipElement.Moved"/> event.
    /// </summary>
    public sealed class ClipMovedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipMovedEventArgs"/> class.
        /// </summary>
        /// <param name="newLayer">The new layer.</param>
        /// <param name="oldLayer">The old layer.</param>
        /// <param name="newStart">The new starting frame.</param>
        /// <param name="oldStart">The old starting frame.</param>
        public ClipMovedEventArgs(int newLayer, int oldLayer, Frame newStart, Frame oldStart)
        {
            NewLayer = newLayer;
            OldLayer = oldLayer;
            NewStart = newStart;
            OldStart = oldStart;
        }

        /// <summary>
        /// Gets the new layer.
        /// </summary>
        public int NewLayer { get; }

        /// <summary>
        /// Gets the old layer.
        /// </summary>
        public int OldLayer { get; }

        /// <summary>
        /// Gets the new starting frame.
        /// </summary>
        public Frame NewStart { get; }

        /// <summary>
        /// Gets the old starting frame.
        /// </summary>
        public Frame OldStart { get; }
    }
}