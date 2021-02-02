
using BEditor.Media;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents a data to be passed to the <see cref="EffectElement"/> at rendering time.
    /// </summary>
    public class EffectRenderArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EffectRenderArgs"/> class.
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
}
