using System.ComponentModel;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public class PropertyElement : EditingObject, IPropertyElement
    {
        private static readonly PropertyChangedEventArgs _metadataArgs = new(nameof(PropertyMetadata));
        private PropertyElementMetadata? _propertyMetadata;

        /// <inheritdoc/>
#pragma warning disable CS8618
        public virtual EffectElement Parent { get; set; }
#pragma warning restore CS8618

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