using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

using BEditor.Core.Data.Property;
namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class SpotLight : EffectElement
    {
        public override string Name => throw new NotImplementedException();
        public override IEnumerable<PropertyElement> Properties => throw new NotImplementedException();
        public override void Render(EffectRenderArgs args) => throw new NotImplementedException();
    }
}
