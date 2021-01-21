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

        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var f = args.Frame;
            var r = (int)Radius[f];
            return Image.RoundRect((int)Width[f], (int)Height[f], (int)Line[f], r, r, Color.Color);
        }
        protected override void OnLoad()
        {
            base.OnLoad();
            Width.Load(WidthMetadata);
            Height.Load(HeightMetadata);
            Radius.Load(RadiusMetadata);
            Line.Load(LineMetadata);
            Color.Load(ColorMetadata);
        }
        protected override void OnUnload()
        {
            base.OnUnload();
            Width.Unload();
            Height.Unload();
            Radius.Unload();
            Line.Unload();
            Color.Unload();
        }
    }
}
