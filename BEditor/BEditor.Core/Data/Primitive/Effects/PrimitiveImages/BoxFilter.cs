using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Media;
using BEditor.Core.Properties;
using BEditor.Drawing;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    public class BoxFilter : ImageEffect
    {
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 70, float.NaN, 0);
        public static readonly CheckPropertyMetadata ResizeMetadata = new(Resources.Diffusion, false);

        public BoxFilter()
        {
            Size = new(SizeMetadata);
            Resize = new(ResizeMetadata);
        }

        public override string Name => Resources.BoxFilter;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            Resize
        };
        [DataMember(Order = 0)]
        public EaseProperty Size { get; private set; }
        [DataMember(Order = 1)]
        public CheckProperty Resize { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var size = (int)Size.GetValue(args.Frame);
            if (Resize.IsChecked)
            {
                var w = args.Value.Width + size;
                var h = args.Value.Height + size;
                args.Value = args.Value.MakeBorder(w, h);
            }

            args.Value = args.Value.BoxBlur(size);
        }
        public override void PropertyLoaded()
        {
            Size.ExecuteLoaded(SizeMetadata);
            Resize.ExecuteLoaded(ResizeMetadata);
        }
    }
}
