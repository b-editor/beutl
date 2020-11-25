using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract(Namespace = "")]
    public class Dilate : ImageEffect
    {
        public static readonly EasePropertyMetadata FrequencyMetadata = new(Resources.Frequency, 1, float.NaN, 0);
        public static readonly CheckPropertyMetadata ResizeMetadata = new(Resources.Resize);

        public Dilate()
        {
            Frequency = new(FrequencyMetadata);
            Resize = new(ResizeMetadata);
        }

        public override string Name => Resources.Dilate;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Frequency,
            Resize
        };
        [DataMember(Order = 0)]
        public EaseProperty Frequency { get; private set; }
        [DataMember(Order = 1)]
        public CheckProperty Resize { get; private set; }

        public override void Render(ref Image source, EffectRenderArgs args)
        {
            var img = source.ToRenderable();
            var size = (int)Frequency.GetValue(args.Frame);
            if (Resize.IsChecked)
            {
                int nwidth = source.Width + (size + 5) * 2;
                int nheight = source.Height + (size + 5) * 2;

                img.AreaExpansion(nwidth, nheight)
                    .Dilate(size);
            }
            else
            {
                img.Dilate(size);
            }
        }
        public override void PropertyLoaded()
        {
            Frequency.ExecuteLoaded(FrequencyMetadata);
            Resize.ExecuteLoaded(ResizeMetadata);
        }
    }
}
