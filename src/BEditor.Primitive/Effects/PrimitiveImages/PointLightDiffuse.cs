using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Properties;

namespace BEditor.Primitive.Effects
{
#pragma warning disable CS1591
    public class PointLightDiffuse : ImageEffect
    {
        public static readonly EasePropertyMetadata XMetadata = Coordinate.XMetadata;
        public static readonly EasePropertyMetadata YMetadata = Coordinate.YMetadata;
        public static readonly EasePropertyMetadata ZMetadata = Coordinate.ZMetadata;
        public static readonly ColorPropertyMetadata LightColorMetadata = new("Light color", Color.Light, true);
        public static readonly EasePropertyMetadata SurfaceScaleMetadata = new("Surface scale", 100, 100, -100);
        public static readonly EasePropertyMetadata LightConstantMetadata = new("Light constant", 100, 100, 0);

        public PointLightDiffuse()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            LightColor = new(LightColorMetadata);
            SurfaceScale = new(SurfaceScaleMetadata);
            LightConstant = new(LightConstantMetadata);
        }

        public override string Name => Resources.PointLightDiffuse;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            LightColor,
            SurfaceScale,
            LightConstant
        };
        [DataMember]
        public EaseProperty X { get; private set; }
        [DataMember]
        public EaseProperty Y { get; private set; }
        [DataMember]
        public EaseProperty Z { get; private set; }
        [DataMember]
        public ColorProperty LightColor { get; private set; }
        [DataMember]
        public EaseProperty SurfaceScale { get; private set; }
        [DataMember]
        public EaseProperty LightConstant { get; private set; }

        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;
            args.Value.PointLightDiffuse(
                new(X[f], Y[f], Z[f]),
                LightColor.Value,
                SurfaceScale[f] / 100,
                LightConstant[f] / 100);
        }
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Z.Load(ZMetadata);
            LightColor.Load(LightColorMetadata);
            SurfaceScale.Load(SurfaceScaleMetadata);
            LightConstant.Load(LightConstantMetadata);
        }
        protected override void OnUnload()
        {
            foreach (var p in Children)
            {
                p.Unload();
            }
        }
    }
#pragma warning restore CS1591
}
