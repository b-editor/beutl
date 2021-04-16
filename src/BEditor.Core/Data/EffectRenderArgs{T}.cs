using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a data to be passed to the <see cref="EffectElement"/> at rendering time.
    /// </summary>
    /// <typeparam name="T">The type of value to pass to the <see cref="EffectElement.Render(EffectRenderArgs)"/> method.</typeparam>
    public class EffectRenderArgs<T> : EffectRenderArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EffectRenderArgs"/> class.
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
}