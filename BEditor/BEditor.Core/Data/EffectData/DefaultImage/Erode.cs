using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Extensions;
using BEditor.Core.Media;

using static BEditor.Core.Data.EffectData.Dilate;

namespace BEditor.Core.Data.EffectData
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

        public override void Render(ref Image source, EffectRenderArgs args) => source.ToRenderable().Erode((int)Frequency.GetValue(args.Frame));

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
