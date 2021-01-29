using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class ChromeKey : ImageEffect
    {
        public static readonly EasePropertyMetadata ThresholdValueMetadata = new("閾値", 256);

        public ChromeKey()
        {
            ThresholdValue = new(ThresholdValueMetadata);
        }

        public override string Name => "Chrome key";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            ThresholdValue
        };
        [DataMember(Order = 0)]
        public EaseProperty ThresholdValue { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.ChromeKey((int)(ThresholdValue[args.Frame]));
        }
        protected override void OnLoad()
        {
            ThresholdValue.Load(ThresholdValueMetadata);
        }
    }
}
