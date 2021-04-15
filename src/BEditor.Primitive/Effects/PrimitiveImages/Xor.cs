using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
#pragma warning disable CS1591
    public sealed class Xor : ImageEffect
    {
        public Xor()
        {
        }

        public override string Name => Strings.Xor;
        public override IEnumerable<PropertyElement> Properties => Array.Empty<PropertyElement>();

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.Xor();
        }
    }
#pragma warning restore CS1591
}