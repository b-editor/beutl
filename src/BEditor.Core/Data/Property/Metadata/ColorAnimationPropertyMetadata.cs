using BEditor.Data.Property.Easing;
using BEditor.Drawing;

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="ColorAnimationProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultColor">The default color.</param>
    /// <param name="DefaultEase">The default easing function.</param>
    /// <param name="UseAlpha">The value of whether to use alpha components or not.</param>
    public record ColorAnimationPropertyMetadata(string Name, Color DefaultColor, EasingMetadata DefaultEase, bool UseAlpha = false)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<ColorAnimationProperty>
    {
        /// <summary>
        /// The metadata of <see cref="ColorAnimationProperty"/>.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        public ColorAnimationPropertyMetadata(string Name)
            : this(Name, default, EasingMetadata.LoadedEasingFunc[0])
        {
        }

        /// <summary>
        /// The metadata of <see cref="ColorAnimationProperty"/>.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultColor">The default color.</param>
        /// <param name="UseAlpha">The value of whether to use alpha components or not.</param>
        public ColorAnimationPropertyMetadata(string Name, Color DefaultColor, bool UseAlpha = false)
            : this(Name, DefaultColor, EasingMetadata.LoadedEasingFunc[0], UseAlpha)
        {
        }

        /// <inheritdoc/>
        public ColorAnimationProperty Create()
        {
            return new(this);
        }
    }
}