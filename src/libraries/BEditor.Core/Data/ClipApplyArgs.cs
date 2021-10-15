// ClipApplyArgs.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Audio;
using BEditor.Drawing;
using BEditor.Graphics;
using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a data to be passed to the <see cref="ClipElement"/> at applying time.
    /// </summary>
    public sealed class ClipApplyArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ClipApplyArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame to apply.</param>
        /// <param name="type">The apply type.</param>
        [Obsolete("Do not use.")]
        public ClipApplyArgs(Frame frame, ApplyType type = ApplyType.Edit)
        {
            Frame = frame;
            Type = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ClipApplyArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="graphicsContext">The graphics context.</param>
        /// <param name="samplingContext">The sampling context.</param>
        /// <param name="drawingContext">The drawing context.</param>
        /// <param name="type">The rendering type.</param>
        public ClipApplyArgs(Frame frame, GraphicsContext graphicsContext, SamplingContext samplingContext, DrawingContext? drawingContext = null, ApplyType type = ApplyType.Edit)
        {
            Frame = frame;
            Type = type;
            Contexts = (graphicsContext, samplingContext, drawingContext);
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
        public ApplyType Type { get; }

        /// <summary>
        /// Gets the contexts.
        /// </summary>
        public (GraphicsContext Graphics, SamplingContext Sampling, DrawingContext? Drawing) Contexts { get; }
    }
}