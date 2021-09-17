// EffectApplyArgs.cs
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
    /// Represents data that is passed to <see cref="EffectElement"/> when it is applied.
    /// </summary>
    public class EffectApplyArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EffectApplyArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="type">The rendering type.</param>
        [Obsolete("Do not use.")]
        public EffectApplyArgs(Frame frame, ApplyType type = ApplyType.Edit)
        {
            Frame = frame;
            Type = type;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EffectApplyArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="contexts">The contexts.</param>
        /// <param name="type">The rendering type.</param>
        public EffectApplyArgs(Frame frame, (GraphicsContext Graphics, SamplingContext Sampling, DrawingContext? Drawing) contexts, ApplyType type = ApplyType.Edit)
        {
            Frame = frame;
            Type = type;
            Contexts = contexts;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EffectApplyArgs"/> class.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="graphicsContext">The graphics context.</param>
        /// <param name="samplingContext">The sampling context.</param>
        /// <param name="drawingContext">The drawing context.</param>
        /// <param name="type">The rendering type.</param>
        public EffectApplyArgs(Frame frame, GraphicsContext graphicsContext, SamplingContext samplingContext, DrawingContext? drawingContext = null, ApplyType type = ApplyType.Edit)
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