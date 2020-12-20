using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;

namespace BEditor.Core.Data.Primitive.Objects
{
    public class AudioObject : ObjectElement
    {
        public override string Name { get; }
        public override IEnumerable<PropertyElement> Properties { get; }

        public override void Render(EffectRenderArgs args) => throw new NotImplementedException();
    }
}
