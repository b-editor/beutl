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
    public class MedianBlur : ImageEffect
    {
        public MedianBlur()
        {
            Size = new(BoxFilter.SizeMetadata);
            Resize = new(BoxFilter.ResizeMetadata);
        }

        public override string Name => Resources.Median;
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
            image.ToRenderable().MedianBlur((int)Size.GetValue(args.Frame), Resize.IsChecked);
        public override void PropertyLoaded()
        {
            Size.ExecuteLoaded(BoxFilter.SizeMetadata);
            Resize.ExecuteLoaded(BoxFilter.ResizeMetadata);
        }
    }
}
