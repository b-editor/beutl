using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.EffectData
{
    [DataContract(Namespace = "")]
    public class Dilate : ImageEffect
    {
        public static readonly EasePropertyMetadata FrequencyMetadata = new(Resources.Frequency, 1, float.NaN, 0);

        public Dilate()
        {
            Frequency = new(FrequencyMetadata);
        }

        #region EffectProperty
        public override string Name => Resources.Dilate;

        public override void Render(ref Image source, EffectRenderArgs args) => source.Dilate((int)Frequency.GetValue(args.Frame));

        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Frequency
        };

        public override void PropertyLoaded()
        {
            Frequency.ExecuteLoaded(FrequencyMetadata);
        }

        #endregion


        [DataMember(Order = 0)]
        public EaseProperty Frequency { get; private set; }
    }
}
