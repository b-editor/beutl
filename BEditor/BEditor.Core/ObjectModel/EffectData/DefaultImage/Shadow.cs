using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.ObjectModel.ProjectData;
using BEditor.ObjectModel.PropertyData;
using BEditor.Media;
using BEditor.Properties;

namespace BEditor.ObjectModel.EffectData
{
    [DataContract(Namespace = "")]
    public class Shadow : ImageEffect
    {
        public static readonly EasePropertyMetadata XMetadata = new(Resources.X, 10);
        public static readonly EasePropertyMetadata YMetadata = new(Resources.Y, 10);
        public static readonly EasePropertyMetadata BlurMetadata = new(Resources.Blur, 10, float.NaN, 0);
        public static readonly EasePropertyMetadata AlphaMetadata = new(Resources.Alpha, 75, 100, 0);
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, 0, 0, 0);

        public Shadow()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Blur = new(BlurMetadata);
            Alpha = new(AlphaMetadata);
            Color = new(ColorMetadata);
        }


        #region EffectProperty
        public override string Name => Resources.DropShadow;

        public override void Render(ref Image source, EffectRenderArgs args) => source.Shadow
            (X.GetValue(args.Frame),
             Y.GetValue(args.Frame),
             (int)Blur.GetValue(args.Frame),
             Alpha.GetValue(args.Frame),
             Color);


        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Blur,
            Alpha,
            Color
        };

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(XMetadata), typeof(Shadow))]
        public EaseProperty X { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(YMetadata), typeof(Shadow))]
        public EaseProperty Y { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(BlurMetadata), typeof(Shadow))]
        public EaseProperty Blur { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(AlphaMetadata), typeof(Shadow))]
        public EaseProperty Alpha { get; private set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(ColorMetadata), typeof(Shadow))]
        public ColorProperty Color { get; private set; }
    }
}
