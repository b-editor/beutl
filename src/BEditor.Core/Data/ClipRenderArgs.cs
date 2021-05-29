// ClipRenderArgs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a data to be passed to the <see cref="ClipElement"/> at rendering time.
    /// </summary>
    public class ClipRenderArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipRenderArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="type">The rendering type.</param>
        public ClipRenderArgs(Frame frame, RenderType type = RenderType.Preview)
        {
            Frame = frame;
            Type = type;
        }

        /// <summary>
        /// Gets the frame to render.
        /// </summary>
        public Frame Frame { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the process has been executed or not.
        /// </summary>
        public bool Handled { get; set; }

        /// <summary>
        /// Gets the rendering type.
        /// </summary>
        public RenderType Type { get; }
    }
}