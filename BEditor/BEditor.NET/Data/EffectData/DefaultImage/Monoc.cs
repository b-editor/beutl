using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Media;

namespace BEditor.NET.Data.EffectData {

    [DataContract(Namespace = "")]
    public class Monoc : ImageEffect {

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

        public override void PropertyLoaded() {
            base.PropertyLoaded();

            Color.PropertyMetadata = ColorMetadata;
        }

        #endregion



        [DataMember(Order = 0)]
        public ColorProperty Color { get; set; }
    }
}
