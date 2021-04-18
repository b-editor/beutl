using System;
using System.ComponentModel;

using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public class PropertyElement : EditingObject, IChild<EffectElement>, IPropertyElement
    {
        private static readonly PropertyChangedEventArgs _metadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata? _propertyMetadata;
        private WeakReference<EffectElement?>? _parent;

        /// <inheritdoc/>
        public virtual EffectElement Parent
        {
            get
            {
                _parent ??= new(null!);

                if (_parent.TryGetTarget(out var p))
                {
                    return p;
                }

                return null!;
            }
            set => (_parent ??= new(null!)).SetTarget(value);
        }

        /// <summary>
        /// Gets or sets the metadata for this <see cref="PropertyElement"/>.
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata
        {
            get => _propertyMetadata;
            set => SetValue(value, ref _propertyMetadata, _metadataArgs);
        }
    }
}