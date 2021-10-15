// ClipSplittedEventArgs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Data
{
    /// <summary>
    /// Provides data for the <see cref="ClipElement.Splitted"/> event.
    /// </summary>
    public sealed class ClipSplittedEventArgs : EventArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipSplittedEventArgs"/> class.
        /// </summary>
        /// <param name="before">The clip before the split frame.</param>
        /// <param name="after">the clip after the split frame.</param>
        public ClipSplittedEventArgs(ClipElement before, ClipElement after)
        {
            Before = before;
            After = after;
        }

        /// <summary>
        /// Gets the clip before the split frame.
        /// </summary>
        public ClipElement Before { get; }

        /// <summary>
        /// Gets the clip after the split frame.
        /// </summary>
        public ClipElement After { get; }
    }
}