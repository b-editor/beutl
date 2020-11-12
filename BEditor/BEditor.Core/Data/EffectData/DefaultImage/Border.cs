using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.EffectData {
    [DataContract(Namespace = "")]
    public class Border : ImageEffect {
        static readonly EasePropertyMetadata SizeMetadata = new EasePropertyMetadata(Properties.Resources.Size, 10, float.NaN, 1);
        static readonly ColorPropertyMetadata ColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 255, 255, 255);

        public Border() {
            Size = new EaseProperty(SizeMetadata);
            Color = new ColorProperty(ColorMetadata);
        }


        #region EffectProperty
        public override string Name => Properties.Resources.Border;

        public override void Draw(ref Image source, EffectRenderArgs args) => source.Border((int)Size.GetValue(args.Frame), Color);


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Size,
            Color
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(SizeMetadata), typeof(Border))]
        public EaseProperty Size { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(ColorMetadata), typeof(Border))]
        public ColorProperty Color { get; set; }
    }
}
