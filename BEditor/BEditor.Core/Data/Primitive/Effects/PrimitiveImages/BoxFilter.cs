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

        public override void Render(ref Image image, EffectRenderArgs args) => 
            image.ToRenderable().Blur((int)Size.GetValue(args.Frame), Resize.IsChecked);
        public override void PropertyLoaded()
        {
            Size.ExecuteLoaded(SizeMetadata);
            Resize.ExecuteLoaded(ResizeMetadata);
        }
    }
}
