using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects.PrimitiveImages
{
    [DataContract]
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

        public override string Name => Resources.DropShadow;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Blur,
            Alpha,
            Color
        };
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Blur { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty Alpha { get; private set; }
        [DataMember(Order = 4)]
        public ColorProperty Color { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value.Shadow(X.GetValue(args.Frame), Y.GetValue(args.Frame), Blur.GetValue(args.Frame), Alpha.GetValue(args.Frame) / 100, Color.Color);
            args.Value.Dispose();

            args.Value = img;
        }

        public override void PropertyLoaded()
        {
            X.ExecuteLoaded(XMetadata);
            Y.ExecuteLoaded(YMetadata);
            Blur.ExecuteLoaded(BlurMetadata);
            Alpha.ExecuteLoaded(AlphaMetadata);
            Color.ExecuteLoaded(ColorMetadata);
        }
    }
}
