using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents data that is passed to <see cref="EffectElement"/> when it is applied.
    /// </summary>
    /// <typeparam name="T">The type of value to pass to the <see cref="EffectElement.Apply(EffectApplyArgs)"/> method.</typeparam>
    public class EffectApplyArgs<T> : EffectApplyArgs
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="EffectApplyArgs"/> class.
        /// </summary>
        public EffectApplyArgs(Frame frame, T value, RenderType type = RenderType.Preview) : base(frame, type)
        {
            Value = value;
        }

        /// <summary>
        /// Gets or sets the value used to apply the effect.
        /// </summary>
        public T Value { get; set; }
    }
}