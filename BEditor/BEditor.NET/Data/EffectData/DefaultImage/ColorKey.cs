using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Media;

namespace BEditor.NET.Data.EffectData {
    [DataContract(Namespace = "")]
    public class ColorKey : ImageEffect {

        static readonly ColorPropertyMetadata MaxColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 255, 255, 255);
        static readonly ColorPropertyMetadata MinColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 100, 100, 100);

        public ColorKey() {
            MaxColor = new ColorProperty(MaxColorMetadata);
            MinColor = new ColorProperty(MinColorMetadata);
        }


        #region ImageEffect
        public override string Name => Properties.Resources.ColorKey;

        public override void Draw(ref Image source, EffectLoadArgs args) { }


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            MaxColor,
            MinColor
        };

        public override void PropertyLoaded() {
            base.PropertyLoaded();

            MaxColor.PropertyMetadata = MaxColorMetadata;
            MinColor.PropertyMetadata = MinColorMetadata;
        }

        #endregion


        [DataMember(Order = 0)]
        public ColorProperty MaxColor { get; set; }

        [DataMember(Order = 1)]
        public ColorProperty MinColor { get; set; }
    }
}
