using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.EffectData
{
    [DataContract(Namespace = "")]
    public class ColorKey : ImageEffect
    {
        static readonly ColorPropertyMetadata MaxColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 255, 255, 255);
        static readonly ColorPropertyMetadata MinColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 100, 100, 100);

        public ColorKey()
        {
            MaxColor = new ColorProperty(MaxColorMetadata);
            MinColor = new ColorProperty(MinColorMetadata);
        }


        #region ImageEffect
        public override string Name => Properties.Resources.ColorKey;

        public override void Draw(ref Image source, EffectRenderArgs args) { }


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            MaxColor,
            MinColor
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(MaxColorMetadata), typeof(ColorKey))]
        public ColorProperty MaxColor { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(MinColorMetadata), typeof(ColorKey))]
        public ColorProperty MinColor { get; set; }
    }
}
