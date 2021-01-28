using System.Collections.Generic;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public interface IPropertyElement : IHasId, IChild<EffectElement>, IElementObject
    {
        /// <summary>
        /// Gets or sets the metadata for this <see cref="IPropertyElement"/>.
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata { get; set; }
        /// <summary>
        /// Get a Dictionary to put the cache in.
        /// </summary>
        public Dictionary<string, dynamic> ComponentData { get; }
    }
}