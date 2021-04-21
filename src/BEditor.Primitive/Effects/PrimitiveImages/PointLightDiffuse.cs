using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
#pragma warning disable CS1591
    public class PointLightDiffuse : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, EaseProperty> XProperty = Coordinate.XProperty.WithOwner<PointLightDiffuse>(
            owner => owner.X,
            (owner, obj) => owner.X = obj);

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, EaseProperty> YProperty = Coordinate.YProperty.WithOwner<PointLightDiffuse>(
            owner => owner.Y,
            (owner, obj) => owner.Y = obj);

        /// <summary>
        /// Defines the <see cref="Z"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, EaseProperty> ZProperty = Coordinate.ZProperty.WithOwner<PointLightDiffuse>(
            owner => owner.Z,
            (owner, obj) => owner.Z = obj);

        /// <summary>
        /// Defines the <see cref="LightColor"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, ColorProperty> LightColorProperty = EditingProperty.RegisterSerializeDirect<ColorProperty, PointLightDiffuse>(
            nameof(LightColor),
            owner => owner.LightColor,
            (owner, obj) => owner.LightColor = obj,
            new ColorPropertyMetadata("Light color", Color.Light, true));

        /// <summary>
        /// Defines the <see cref="SurfaceScale"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, EaseProperty> SurfaceScaleProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, PointLightDiffuse>(
            nameof(SurfaceScale),
            owner => owner.SurfaceScale,
            (owner, obj) => owner.SurfaceScale = obj,
            new EasePropertyMetadata("Surface scale", 100, 100, -100));

        /// <summary>
        /// Defines the <see cref="LightConstant"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, EaseProperty> LightConstantProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, PointLightDiffuse>(
            nameof(LightConstant),
            owner => owner.LightConstant,
            (owner, obj) => owner.LightConstant = obj,
            new EasePropertyMetadata("Light constant", 100, 100, 0));

#pragma warning disable CS8618
        public PointLightDiffuse()
#pragma warning restore CS8618
        {
        }

        public override string Name => Strings.PointLightDiffuse;

        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return X;
                yield return Y;
                yield return Z;
                yield return LightColor;
                yield return SurfaceScale;
                yield return LightConstant;
            }
        }

        public EaseProperty X { get; private set; }

        public EaseProperty Y { get; private set; }

        public EaseProperty Z { get; private set; }

        public ColorProperty LightColor { get; private set; }

        public EaseProperty SurfaceScale { get; private set; }

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
    }
#pragma warning restore CS1591
}