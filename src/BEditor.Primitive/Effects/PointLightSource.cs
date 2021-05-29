// PointLightSource.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="EffectElement"/> that sets the OpenGL point light source.
    /// </summary>
    public sealed class PointLightSource : EffectElement
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, EaseProperty> XProperty = Coordinate.XProperty.WithOwner<PointLightSource>(
            owner => owner.X,
            (owner, obj) => owner.X = obj);

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, EaseProperty> YProperty = Coordinate.YProperty.WithOwner<PointLightSource>(
            owner => owner.Y,
            (owner, obj) => owner.Y = obj);

        /// <summary>
        /// Defines the <see cref="Z"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, EaseProperty> ZProperty = Coordinate.ZProperty.WithOwner<PointLightSource>(
            owner => owner.Z,
            (owner, obj) => owner.Z = obj);

        /// <summary>
        /// Defines the <see cref="Ambient"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, ColorAnimationProperty> AmbientProperty = Material.AmbientProperty.WithOwner<PointLightSource>(
            owner => owner.Ambient,
            (owner, obj) => owner.Ambient = obj);

        /// <summary>
        /// Defines the <see cref="Diffuse"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, ColorAnimationProperty> DiffuseProperty = Material.DiffuseProperty.WithOwner<PointLightSource>(
            owner => owner.Diffuse,
            (owner, obj) => owner.Diffuse = obj);

        /// <summary>
        /// Defines the <see cref="Specular"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<PointLightSource, ColorAnimationProperty> SpecularProperty = Material.SpecularProperty.WithOwner<PointLightSource>(
            owner => owner.Specular,
            (owner, obj) => owner.Specular = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="PointLightSource"/> class.
        /// </summary>
        public PointLightSource()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.PointLightSource;

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
        /// Gets the ambient.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Ambient { get; private set; }

        /// <summary>
        /// Gets the diffuse.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Diffuse { get; private set; }

        /// <summary>
        /// Gets the specular.
        /// </summary>
        [AllowNull]
        public ColorAnimationProperty Specular { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            var frame = args.Frame;

            Parent!.Parent!.GraphicsContext!.Light = new(
                new(X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame)),
                Ambient[frame],
                Diffuse[frame],
                Specular[frame]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return X;
            yield return Y;
            yield return Z;
            yield return Ambient;
            yield return Diffuse;
            yield return Specular;
        }
    }
}