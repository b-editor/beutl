using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Media;

using static BEditor.ObjectModel.EffectData.Dilate;

namespace BEditor.ObjectModel.EffectData
{
    [DataContract(Namespace = "")]
    public class Erode : ImageEffect
    {
        public Erode()
        {
            Frequency = new(FrequencyMetadata);
        }


        #region EffectProperty
        public override string Name => Core.Properties.Resources.Erode;

        public override void Render(ref Image source, EffectRenderArgs args) => source.Erode((int)Frequency.GetValue(args.Frame));


        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Frequency
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(FrequencyMetadata), typeof(Dilate))]
        public EaseProperty Frequency { get; private set; }
    }
}
