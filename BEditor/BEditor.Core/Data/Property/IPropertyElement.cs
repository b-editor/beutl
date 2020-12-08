using System.Collections.Generic;

using BEditor.Core.Data.Control;

namespace BEditor.Core.Data.Property
{
    public interface IPropertyElement : IHadId, IChild<EffectElement>
    {
        public PropertyElementMetadata PropertyMetadata { get; set; }
        public Dictionary<string, dynamic> ComponentData { get; }

        public void PropertyLoaded();
    }
}