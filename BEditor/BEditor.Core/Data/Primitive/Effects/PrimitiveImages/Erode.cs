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
        }


        #region Properties

        public override string Name => Core.Properties.Resources.Erode;

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Frequency
        };


        [DataMember(Order = 0)]
        public EaseProperty Frequency { get; private set; }

        #endregion


        public override void Render(ref Image source, EffectRenderArgs args) =>
            source.ToRenderable()
                .Erode((int)Frequency.GetValue(args.Frame));

        public override void PropertyLoaded()
        {
            Frequency.ExecuteLoaded(FrequencyMetadata);
        }
    }
}
