using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Media;

using static BEditor.Core.Data.Primitive.Effects.PrimitiveImages.Dilate;
using BEditor.Core.Command;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract(Namespace = "")]
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

        public override void Render(ref Image source, EffectRenderArgs args)
        {
            var img = source.ToRenderable();
            var size = (int)Frequency.GetValue(args.Frame);

            if (Resize.IsChecked)
            {
                int nwidth = source.Width - (size + 5) * 2;
                int nheight = source.Height - (size + 5) * 2;

                img.AreaExpansion(nwidth, nheight).Erode(size);
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
