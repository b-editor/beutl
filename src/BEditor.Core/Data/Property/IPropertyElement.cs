using System;
using System.Collections.Generic;
using System.Globalization;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public interface IPropertyElement : IHasId, IChild<EffectElement>, IElementObject, IEditingObject, IJsonObject, IFormattable
    {
        /// <summary>
        /// Gets or sets the metadata for this <see cref="IPropertyElement"/>.
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata { get; set; }

        /// <inheritdoc cref="IFormattable.ToString(string?, IFormatProvider?)"/>
        public string ToString(string? format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }
    }
}