// ValuePropertyMetadata.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// The metadata of <see cref="ValueProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultValue">The default value.</param>
    /// <param name="Max">The maximum value.</param>
    /// <param name="Min">The minimum value.</param>
    public record ValuePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN)
        : PropertyElementMetadata(Name), IEditingPropertyInitializer<ValueProperty>
    {
        /// <inheritdoc/>
        public ValueProperty Create()
        {
            return new(this);
        }
    }
}