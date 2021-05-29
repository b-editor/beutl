// PointLightDiffuse.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Point light diffusion effect.
    /// </summary>
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
        public static readonly DirectEditingProperty<PointLightDiffuse, ColorProperty> LightColorProperty = EditingProperty.RegisterDirect<ColorProperty, PointLightDiffuse>(
            nameof(LightColor),
            owner => owner.LightColor,
            (owner, obj) => owner.LightColor = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata("Light color", Colors.White, true)).Serialize());

        /// <summary>
        /// Defines the <see cref="SurfaceScale"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, EaseProperty> SurfaceScaleProperty = EditingProperty.RegisterDirect<EaseProperty, PointLightDiffuse>(
            nameof(SurfaceScale),
            owner => owner.SurfaceScale,
            (owner, obj) => owner.SurfaceScale = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Surface scale", 100, 100, -100)).Serialize());

        /// <summary>
        /// Defines the <see cref="LightConstant"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightDiffuse, EaseProperty> LightConstantProperty = EditingProperty.RegisterDirect<EaseProperty, PointLightDiffuse>(
            nameof(LightConstant),
            owner => owner.LightConstant,
            (owner, obj) => owner.LightConstant = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Light constant", 100, 100, 0)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="PointLightDiffuse"/> class.
        /// </summary>
        public PointLightDiffuse()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.PointLightDiffuse;

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the Z coordinate.
        /// </summary>
        [AllowNull]
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Gets the color of light.
        /// </summary>
        [AllowNull]
        public ColorProperty LightColor { get; private set; }

        /// <summary>
        /// Gets the scale factor to transform from alpha values to physical height.
        /// </summary>
        [AllowNull]
        public EaseProperty SurfaceScale { get; private set; }

        /// <summary>
        /// Gets the diffuse reflectance coefficient.
        /// </summary>
        [AllowNull]
        public EaseProperty LightConstant { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var f = args.Frame;
            args.Value.PointLightDiffuse(
                new(X[f], Y[f], Z[f]),
                LightColor.Value,
                SurfaceScale[f] / 100,
                LightConstant[f] / 100);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return X;
            yield return Y;
            yield return Z;
            yield return LightColor;
            yield return SurfaceScale;
            yield return LightConstant;
        }
    }
}