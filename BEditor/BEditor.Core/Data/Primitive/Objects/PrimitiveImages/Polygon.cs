using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using static BEditor.Core.Data.Primitive.Objects.Figure;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    [CustomClipUI(Color = 0x0091ea)]
    public class Polygon : ImageObject
    {
        public static readonly ValuePropertyMetadata NumberMetadata = new("角", 3, Min: 3);

        public Polygon()
        {
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Number = new(NumberMetadata);
            Color = new(ColorMetadata);
        }

        public override string Name => "Polygon";
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Width,
            Height,
            Number,
            Color
        };
        [DataMember(Order = 0)]
        public EaseProperty Width { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Height { get; private set; }
        [DataMember(Order = 2)]
        public ValueProperty Number { get; private set; }
        [DataMember(Order = 3)]
        public ColorProperty Color { get; private set; }

        public override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var width = (int)Width.GetValue(args.Frame);
            var height = (int)Height.GetValue(args.Frame);

            if (width <= 0 || height <= 0) return new(1, 1, default(BGRA32));

            return Drawing.Image.Polygon(
                (int)Number.Value,
                width,
                height,
                Color.Color);
        }
        public override void Loaded()
        {
            base.Loaded();

            Width.ExecuteLoaded(WidthMetadata);
            Height.ExecuteLoaded(HeightMetadata);
            Number.ExecuteLoaded(NumberMetadata);
            Color.ExecuteLoaded(ColorMetadata);
        }
        public override void Unloaded()
        {
            base.Unloaded();

            Width.Unloaded();
            Height.Unloaded();
            Number.Unloaded();
            Color.Unloaded();
        }
    }
}
