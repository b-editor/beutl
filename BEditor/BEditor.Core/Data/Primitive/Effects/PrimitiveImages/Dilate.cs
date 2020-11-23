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


        public Dilate()
        {
            Frequency = new(FrequencyMetadata);
        }


        #region Properties

        public override string Name => Resources.Dilate;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Frequency
        };


        [DataMember(Order = 0)]
        public EaseProperty Frequency { get; private set; }

        #endregion


        public override void Render(ref Image source, EffectRenderArgs args) =>
            source.ToRenderable()
                .Dilate((int)Frequency.GetValue(args.Frame));

        public override void PropertyLoaded()
        {
            Frequency.ExecuteLoaded(FrequencyMetadata);
        }
    }
}
