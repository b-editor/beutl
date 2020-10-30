using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET;
using BEditor.NET.Data.ObjectData;
using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Media;

namespace BEditor.NET.Data.EffectData {

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

        public override void Draw(ref Image source, EffectLoadArgs args) => source.Border((int)Size.GetValue(args.Frame), Color);


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            Size,
            Color
        };

        public override void PropertyLoaded() {
            base.PropertyLoaded();

            Size.PropertyMetadata = SizeMetadata;
            Color.PropertyMetadata = ColorMetadata;
        }

        #endregion


        [DataMember(Order = 0)]
        public EaseProperty Size { get; set; }

        [DataMember(Order = 1)]
        public ColorProperty Color { get; set; }
    }
}
