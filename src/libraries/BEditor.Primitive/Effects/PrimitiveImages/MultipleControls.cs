// MultipleControls.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents a <see cref="ImageEffect"/> that provides the ability to edit multiple objects by specifying their indices.
    /// </summary>
    public sealed class MultipleControls : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Coordinate"/> property.
        /// </summary>
        public static readonly DirectProperty<MultipleControls, Coordinate> CoordinateProperty = ImageObject.CoordinateProperty.WithOwner<MultipleControls>(
            owner => owner.Coordinate,
            (owner, obj) => owner.Coordinate = obj);

        /// <summary>
        /// Defines the <see cref="Scale"/> property.
        /// </summary>
        public static readonly DirectProperty<MultipleControls, Scale> ScaleProperty = ImageObject.ScaleProperty.WithOwner<MultipleControls>(
            owner => owner.Scale,
            (owner, obj) => owner.Scale = obj);

        /// <summary>
        /// Defines the <see cref="Rotate"/> property.
        /// </summary>
        public static readonly DirectProperty<MultipleControls, Rotate> RotateProperty = ImageObject.RotateProperty.WithOwner<MultipleControls>(
            owner => owner.Rotate,
            (owner, obj) => owner.Rotate = obj);

        /// <summary>
        /// Defines the <see cref="Index"/> property.
        /// </summary>
        public static readonly DirectProperty<MultipleControls, ValueProperty> IndexProperty = EditingProperty.RegisterDirect<ValueProperty, MultipleControls>(
            nameof(Index),
            owner => owner.Index,
            (owner, obj) => owner.Index = obj,
            EditingPropertyOptions<ValueProperty>.Create(new ValuePropertyMetadata("index", 0, Min: 0)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="MultipleControls"/> class.
        /// </summary>
        public MultipleControls()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.MultipleImageControls;

        /// <summary>
        /// Get the coordinates.
        /// </summary>
        [AllowNull]
        public Coordinate Coordinate { get; private set; }

        /// <summary>
        /// Get the scale.
        /// </summary>
        [AllowNull]
        public Scale Scale { get; private set; }

        /// <summary>
        /// Get the angle.
        /// </summary>
        [AllowNull]
        public Rotate Rotate { get; private set; }

        /// <summary>
        /// Gets the index of the image to be controlled.
        /// </summary>
        [AllowNull]
        public ValueProperty Index { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.Select((texture, i) =>
            {
                if (i == (int)Index.Value)
                {
                    // テクスチャをコピー
                    using var image = texture.ToImage();
                    var result = Texture.FromImage(image, texture.Vertices);
                    result.Synchronize(texture);

                    // Transformを設定
                    var f = args.Frame;
                    var s = Scale.Scale1[f] / 100;
                    var sx = Scale.ScaleX[f] / 100 * s;
                    var sy = Scale.ScaleY[f] / 100 * s;
                    var sz = Scale.ScaleZ[f] / 100 * s;
                    var transform = result.Transform;
                    transform.Coordinate += new Vector3(Coordinate.X[f], Coordinate.Y[f], Coordinate.Z[f]);
                    transform.Center += new Vector3(Coordinate.CenterX[f], Coordinate.CenterY[f], Coordinate.CenterZ[f]);
                    transform.Rotate += new Vector3(Rotate.RotateX[f], Rotate.RotateY[f], Rotate.RotateZ[f]);
                    transform.Scale *= new Vector3(sx, sy, sz);
                    result.Transform = transform;

                    texture.Dispose();
                    return result;
                }

                return texture;
            });
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Coordinate;
            yield return Scale;
            yield return Rotate;
            yield return Index;
        }
    }
}