using System.Collections.Generic;

using BEditor.Core.Data.Control;

namespace BEditor.Core.Data.Property
{
    public interface IPropertyElement : IHasId, IChild<EffectElement>, IElementObject
    {
        public PropertyElementMetadata PropertyMetadata { get; set; }
        public Dictionary<string, dynamic> ComponentData { get; }
    }
}