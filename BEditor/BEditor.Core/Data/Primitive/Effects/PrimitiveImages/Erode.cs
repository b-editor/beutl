using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Media;

using static BEditor.Core.Data.Primitive.Effects.PrimitiveImages.Dilate;
using BEditor.Core.Command;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract]
    public class Erode : ImageEffect
    {
        public Erode()
        {
            Frequency = new(FrequencyMetadata);
            Resize = new(ResizeMetadata);
        }

        public override string Name => Core.Properties.Resources.Erode;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Frequency,
            Resize
        };
        [DataMember(Order = 0)]
        public EaseProperty Frequency { get; private set; }
        [DataMember(Order =1)]
        public CheckProperty Resize { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value;
            var size = (int)Frequency.GetValue(args.Frame);

            if (Resize.IsChecked)
            {
                int nwidth = img.Width - (size + 5) * 2;
                int nheight = img.Height - (size + 5) * 2;

                args.Value = img.MakeBorder(nwidth, nheight);
                args.Value.Erode(size);

                img.Dispose();
            }
            else
            {
                img.Erode(size);
            }
        }
        public override void PropertyLoaded()
        {
            Frequency.ExecuteLoaded(FrequencyMetadata);
            Resize.ExecuteLoaded(ResizeMetadata);
        }
    }
}
