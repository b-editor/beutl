using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using static BEditor.Core.Data.Primitive.Effects.Dilate;

namespace BEditor.Core.Data.Primitive.Effects
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
        [DataMember(Order = 1)]
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
        protected override void OnLoad()
        {
            Frequency.Load(FrequencyMetadata);
            Resize.Load(ResizeMetadata);
        }
        protected override void OnUnload()
        {
            foreach (var pr in Children)
            {
                pr.Unload();
            }
        }
    }
}
