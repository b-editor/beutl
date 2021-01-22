using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Core.Service;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    [CustomClipUI(Color = 0x0091ea)]
    public class Figure : ImageObject
    {
        public static readonly EasePropertyMetadata WidthMetadata = new(Resources.Width, 100, float.NaN, 0);
        public static readonly EasePropertyMetadata HeightMetadata = new(Resources.Height, 100, float.NaN, 0);
        public static readonly EasePropertyMetadata LineMetadata = new(Resources.Line, 4000, float.NaN, 0);
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light);
        public static readonly SelectorPropertyMetadata TypeMetadata = new(Resources.Type, new string[]
        {
            Resources.Circle,
            Resources.Square
        });

        public Figure()
        {
            Width = new(WidthMetadata);
            Height = new(HeightMetadata);
            Line = new(LineMetadata);
            Color = new(ColorMetadata);
            Type = new(TypeMetadata);
        }

        public override string Name => Resources.Figure;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Width,
            Height,
            Line,
            Color,
            Type
        };
        [DataMember(Order = 0)]
        public EaseProperty Width { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Height { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Line { get; private set; }
        [DataMember(Order = 3)]
        public ColorProperty Color { get; private set; }
        [DataMember(Order = 4)]
        public SelectorProperty Type { get; private set; }

        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var width = (int)Width[args.Frame];
            var height = (int)Height[args.Frame];

            if (width <= 0 || height <= 0) return new(1, 1, default(BGRA32));

            if (Type.Index == 0)
            {
                return Image.Ellipse(width, height, (int)Line[args.Frame], Color.Color);
            }
            else
            {
                return Image.Rect(width, height, (int)Line[args.Frame], Color.Color);
            }
        }
        protected override void OnLoad()
        {
            base.OnLoad();
            Width.Load(WidthMetadata);
            Height.Load(HeightMetadata);
            Line.Load(LineMetadata);
            Color.Load(ColorMetadata);
            Type.Load(TypeMetadata);
        }
        protected override void OnUnload()
        {
            base.OnUnload();
            Width.Unload();
            Height.Unload();
            Line.Unload();
            Color.Unload();
            Type.Unload();
        }
    }
}
