using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using static BEditor.Core.Data.Primitive.Objects.PrimitiveImages.Figure;

namespace BEditor.Core.Data.Primitive.Objects.PrimitiveImages
{
    [DataContract]
    [CustomClipUI(Color = 0x0091ea)]
    public class RoundRect : ImageObject
    {
        public static readonly EasePropertyMetadata RadiusMetadata = new("Radius", 20, Min: 0);

        public RoundRect()
        {
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Radius = new(RadiusMetadata);
            Line = new(LineMetadata);
            Color = new(ColorMetadata);
        }

        public override string Name => "RoundRect";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Width,
            Height,
            Radius,
            Line,
            Color
        };
        [DataMember(Order = 0)]
        public EaseProperty Width { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Height { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Radius { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty Line { get; private set; }
        [DataMember(Order = 4)]
        public ColorProperty Color { get; private set; }

        public override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var f = args.Frame;
            var r = (int)Radius.GetValue(f);
            return Drawing.Image.RoundRect(
                (int)Width.GetValue(f),
                (int)Height.GetValue(f),
                (int)Line.GetValue(f),
                r,
                r,
                Color.Color);
        }
        public override void Loaded()
        {
            base.Loaded();
            Width.ExecuteLoaded(WidthMetadata);
            Height.ExecuteLoaded(HeightMetadata);
            Radius.ExecuteLoaded(RadiusMetadata);
            Line.ExecuteLoaded(LineMetadata);
            Color.ExecuteLoaded(ColorMetadata);
        }
        public override void Unloaded()
        {
            base.Unloaded();

            Width.Unloaded();
            Height.Unloaded();
            Radius.Unloaded();
            Line.Unloaded();
            Color.Unloaded();
        }
    }
}
