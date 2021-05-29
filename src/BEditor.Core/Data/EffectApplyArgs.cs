// EffectApplyArgs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents data that is passed to <see cref="EffectElement"/> when it is applied.
    /// </summary>
    public class EffectApplyArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EffectApplyArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="type">The rendering type.</param>
        public EffectApplyArgs(Frame frame, RenderType type = RenderType.Preview)
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