using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditorCore;
using BEditorCore.Data.ProjectData;
using BEditorCore.Data.PropertyData;
using BEditorCore.Media;

using static BEditorCore.Data.EffectData.Dilate;

namespace BEditorCore.Data.EffectData {

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
