using System;
using System.Collections.Generic;

using BEditor.Media;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents a data to be passed to the <see cref="ClipData"/> at rendering time.
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
    /// Represents a data to be passed to the <see cref="EffectElement"/> at rendering time.
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
    /// Represents a data to be passed to the <see cref="EffectElement"/> at rendering time.
    /// </summary>
    public class EffectRenderArgs<T> : EffectRenderArgs
    {
        /// <summary>
        /// <see cref="EffectRenderArgs"/> Initialize a new instance of the class.
        /// </summary>
        public EffectRenderArgs(Frame frame, T value, RenderType type = RenderType.Preview) : base(frame, type)
        {
            Value = value;
        }

        /// <summary>
        /// Gets or sets the value used to render the effect.
        /// </summary>
        public T Value { get; set; }
    }
    public enum RenderType
    {
        Preview,
        VideoPreview,
        ImageOutput,
        VideoOutput
    }
    /// <summary>
    /// Represents errors that occur during rendering.
    /// </summary>
    public class RenderingException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RenderingException"/> class.
        /// </summary>
        public RenderingException() { }
        /// <summary>
        /// Initializes a new instance of the <see cref="RenderingException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        public RenderingException(string? message) : base(message) { }
        /// <summary>
        /// Initializes a new instance of the <see cref="RenderingException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception, or a null reference(Nothing in Visual Basic) if no inner exception is specified.</param>
        public RenderingException(string? message, Exception? innerException) : base(message, innerException) { }
    }
}
