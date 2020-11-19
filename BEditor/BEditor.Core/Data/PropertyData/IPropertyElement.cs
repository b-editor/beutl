using System.Collections.Generic;

using BEditor.Core.Data.EffectData;

namespace BEditor.Core.Data.PropertyData
{
    public interface IPropertyElement : IHadId, IChild<EffectElement>
    {
        public PropertyElementMetadata PropertyMetadata { get; set; }
        public Dictionary<string, dynamic> ComponentData { get; }

        public void PropertyLoaded();
    }
}