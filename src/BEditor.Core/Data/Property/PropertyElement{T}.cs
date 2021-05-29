// PropertyElement{T}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <inheritdoc cref="PropertyElement"/>
    /// <typeparam name="T">Type of <see cref="PropertyMetadata"/>.</typeparam>
    public abstract class PropertyElement<T> : PropertyElement
        where T : PropertyElementMetadata
    {
        /// <inheritdoc cref="PropertyElement.PropertyMetadata"/>
        public new T? PropertyMetadata
        {
            get => base.PropertyMetadata as T;
            set => base.PropertyMetadata = value;
        }
    }
}