using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.EffectData {
    [DataContract(Namespace = "")]
    public sealed class Dilate : ImageEffect {
        public static readonly EasePropertyMetadata FrequencyMetadata = new EasePropertyMetadata(Properties.Resources.Frequency, 1, float.NaN, 0);

        public Dilate() {
            Frequency = new EaseProperty(FrequencyMetadata);
        }

        #region EffectProperty
        public override string Name => Properties.Resources.Dilate;

        public override void Draw(ref Image source, EffectLoadArgs args) => source.Dilate((int)Frequency.GetValue(args.Frame));


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> { Frequency };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(FrequencyMetadata), typeof(Dilate))]
        public EaseProperty Frequency { get; set; }
    }
}
