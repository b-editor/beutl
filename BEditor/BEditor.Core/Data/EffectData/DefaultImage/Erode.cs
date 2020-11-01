using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

using static BEditor.Core.Data.EffectData.Dilate;

namespace BEditor.Core.Data.EffectData {

    [DataContract(Namespace = "")]
    public class Erode : ImageEffect {


        public Erode() {
            Frequency = new EaseProperty(FrequencyMetadata);
        }


        #region EffectProperty
        public override string Name => Properties.Resources.Erode;

        public override void Draw(ref Image source, EffectLoadArgs args) => source.Erode((int)Frequency.GetValue(args.Frame));


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Frequency
        };

        public override void PropertyLoaded() {
            base.PropertyLoaded();

            Frequency.PropertyMetadata = FrequencyMetadata;
        }

        #endregion


        [DataMember(Order = 0)]
        public EaseProperty Frequency { get; set; }
    }
}
