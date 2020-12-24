using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract]
    public class Blur : ImageEffect
    {
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 25, float.NaN, 0);

        public Blur()
        {
            Size = new(SizeMetadata);
        }

        public override string Name => Resources.Blur;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size
        };
        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var size = (int)Size.GetValue(args.Frame);
            if (size is 0) return;

            args.Value.Blur(size);
        }
        public override void PropertyLoaded()
        {
            Size.ExecuteLoaded(SizeMetadata);
        }
    }
}
