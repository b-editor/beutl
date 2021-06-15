// PropertyElement.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.ComponentModel;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public class PropertyElement : EditingObject, IPropertyElement, IHasMetadata
    {
        private static readonly PropertyChangedEventArgs _metadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata? _propertyMetadata;

        /// <inheritdoc/>
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public virtual EffectElement Parent { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        /// <summary>
        /// Gets or sets the metadata for this <see cref="PropertyElement"/>.
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata
        {
            get => _propertyMetadata;
            set => SetAndRaise(value, ref _propertyMetadata, _metadataArgs);
        }

        /// <inheritdoc/>
        object? IHasMetadata.Metadata
        {
            get => PropertyMetadata;
            set => PropertyMetadata ??= value as PropertyElementMetadata;
        }
    }
}