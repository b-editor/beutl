using BEditor.Data.Property.Easing;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="EaseProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultEase">The default easing function.</param>
    /// <param name="DefaultValue">The default value.</param>
    /// <param name="Max">The maximum value.</param>
    /// <param name="Min">The minimum value.</param>
    /// <param name="UseOptional">The bool of whether to use the Optional value.</param>
    public record EasePropertyMetadata(string Name, EasingMetadata DefaultEase, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<EaseProperty>
    {
        /// <summary>
        /// The metadata of <see cref="EaseProperty"/>.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultValue">The default value.</param>
        /// <param name="Max">The maximum value.</param>
        /// <param name="Min">The minimum value.</param>
        /// <param name="UseOptional">The bool of whether to use the Optional value.</param>
        public EasePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false)
            : this(Name, EasingMetadata.LoadedEasingFunc[0], DefaultValue, Max, Min, UseOptional)
        {
        }

        /// <inheritdoc/>
        public EaseProperty Create()
        {
            return new(this);
        }
    }
}