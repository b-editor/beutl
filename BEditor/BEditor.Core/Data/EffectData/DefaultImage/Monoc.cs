using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.EffectData {
    [DataContract(Namespace = "")]
    public sealed class Monoc : ImageEffect {
        static readonly ColorPropertyMetadata ColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 255, 255, 255);

        public Monoc() {
            Color = new ColorProperty(ColorMetadata);
        }


        #region EffectProperty
        public override string Name => Properties.Resources.Monoc;

        public override void Draw(ref Image source, EffectLoadArgs args) => source.SetColor(Color);


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Color
        };

        #endregion



        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(ColorMetadata),typeof(Monoc))]
        public ColorProperty Color { get; set; }
    }
}
