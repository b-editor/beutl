using System.Collections.Generic;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property used by <see cref="EffectElement"/>.
    /// </summary>
    public interface IPropertyElement : IHasId, IChild<EffectElement>, IElementObject, IEditorObject, IJsonObject
    {
        /// <summary>
        /// Gets or sets the metadata for this <see cref="IPropertyElement"/>.
        /// </summary>
        public PropertyElementMetadata? PropertyMetadata { get; set; }
    }
}