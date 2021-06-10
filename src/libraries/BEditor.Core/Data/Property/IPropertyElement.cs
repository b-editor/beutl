// IPropertyElement.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public interface IPropertyElement : IChild<EffectElement>, IElementObject, IEditingObject, IJsonObject
    {
        /// <summary>
        /// Gets or sets the metadata for this <see cref="IPropertyElement"/>.
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata { get; set; }
    }
}