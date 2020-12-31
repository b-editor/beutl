using System.Collections.Generic;

using BEditor.Media;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the data to be passed to the <see cref="ClipData"/> at rendering time.
    /// </summary>
    public class ClipRenderArgs
    {
        /// <summary>
        /// <see cref="ClipRenderArgs"/> Initialize a new instance of the class.
        /// </summary>
        public ClipRenderArgs(Frame frame, RenderType type = RenderType.Preview)
        {
            Frame = frame;
            Type = type;
        }

        /// <summary>
        /// Get the frame to render.
        /// </summary>
        public Frame Frame { get; }
        /// <summary>
        /// Gets or sets a value that indicates the current state of the process.
        /// </summary>
        public bool Handled { get; set; }
        /// <summary>
        /// Get the rendering type.
        /// </summary>
        public RenderType Type { get; }
    }

    /// <summary>
    /// Represents the data to be passed to the <see cref="EffectElement"/> at rendering time.
    /// </summary>
    public class EffectRenderArgs
    {
        /// <summary>
        /// <see cref="EffectRenderArgs"/> Initialize a new instance of the class.
        /// </summary>
        public EffectRenderArgs(Frame frame, RenderType type = RenderType.Preview)
        {
            Frame = frame;
            Type = type;
        }

        /// <summary>
        /// Get the frame to render.
        /// </summary>
        public Frame Frame { get; }
        /// <summary>
        /// Gets or sets a value that indicates the current state of the process.
        /// </summary>
        public bool Handled { get; set; }
        /// <summary>
        /// Get the rendering type.
        /// </summary>
        public RenderType Type { get; }
    }
    /// <summary>
    /// Represents the data to be passed to the <see cref="EffectElement"/> at rendering time.
    /// </summary>
    public class EffectRenderArgs<T> : EffectRenderArgs
    {
        /// <summary>
        /// <see cref="EffectRenderArgs"/> Initialize a new instance of the class.
        /// </summary>
        public EffectRenderArgs(Frame frame, RenderType type = RenderType.Preview) : base(frame, type)
        {
        }

        /// <summary>
        /// Gets or sets the value used to render the effect.
        /// </summary>
        public T Value { get; set; }
    }
    public enum RenderType
    {
        Preview,
        ImageOutput,
        VideoOutput
    }
}
