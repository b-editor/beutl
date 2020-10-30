using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET;
using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Media;

namespace BEditor.NET.Data.EffectData {

    [DataContract(Namespace = "")]
    public class Shadow : ImageEffect {

        static readonly EasePropertyMetadata XMetadata = new EasePropertyMetadata(Properties.Resources.X, 10);
        static readonly EasePropertyMetadata YMetadata = new EasePropertyMetadata(Properties.Resources.Y, 10);
        static readonly EasePropertyMetadata BlurMetadata = new EasePropertyMetadata(Properties.Resources.Blur, 10, float.NaN, 0);
        static readonly EasePropertyMetadata AlphaMetadata = new EasePropertyMetadata(Properties.Resources.Alpha, 75, 100, 0);
        static readonly ColorPropertyMetadata ColorMetadata = new ColorPropertyMetadata(Properties.Resources.Color, 0, 0, 0);

        public Shadow() {
            X = new EaseProperty(XMetadata);
            Y = new EaseProperty(YMetadata);
            Blur = new EaseProperty(BlurMetadata);
            Alpha = new EaseProperty(AlphaMetadata);
            Color = new ColorProperty(ColorMetadata);
        }


        #region EffectProperty
        public override string Name => Properties.Resources.DropShadow;

        public override void Draw(ref Image source, EffectLoadArgs args) => source.Shadow
            (X.GetValue(args.Frame),
             Y.GetValue(args.Frame),
             (int)Blur.GetValue(args.Frame),
             Alpha.GetValue(args.Frame),
             Color);


        public override IList<PropertyElement> PropertySettings => new List<PropertyElement> {
            X,
            Y,
            Blur,
            Alpha,
            Color
        };

        public override void PropertyLoaded() {
            base.PropertyLoaded();

            X.PropertyMetadata = XMetadata;
            Y.PropertyMetadata = YMetadata;
            Blur.PropertyMetadata = BlurMetadata;
            Alpha.PropertyMetadata = AlphaMetadata;
            Color.PropertyMetadata = ColorMetadata;
        }
        #endregion


        [DataMember(Order = 0)]
        public EaseProperty X { get; set; }

        [DataMember(Order = 1)]
        public EaseProperty Y { get; set; }

        [DataMember(Order = 2)]
        public EaseProperty Blur { get; set; }

        [DataMember(Order = 3)]
        public EaseProperty Alpha { get; set; }

        [DataMember(Order = 4)]
        public ColorProperty Color { get; set; }
    }
}
