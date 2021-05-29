// EasePropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
        /// Initializes a new instance of the <see cref="EasePropertyMetadata"/> class.
        /// </summary>
        /// <param name="name">The string displayed in the property header.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <param name="max">The maximum value.</param>
        /// <param name="min">The minimum value.</param>
        /// <param name="useOptional">The bool of whether to use the Optional value.</param>
        public EasePropertyMetadata(string name, float defaultValue = 0, float max = float.NaN, float min = float.NaN, bool useOptional = false)
            : this(name, EasingMetadata.LoadedEasingFunc[0], defaultValue, max, min, useOptional)
        {
        }

        /// <inheritdoc/>
        public EaseProperty Create()
        {
            return new(this);
        }
    }
}