// Clipping.cs
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
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that cripping the image.
    /// </summary>
    public sealed class Clipping : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Top"/> property.
        /// </summary>
        public static readonly DirectProperty<Clipping, EaseProperty> TopProperty = EditingProperty.RegisterDirect<EaseProperty, Clipping>(
            nameof(Top),
            owner => owner.Top,
            (owner, obj) => owner.Top = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Top, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Bottom"/> property.
        /// </summary>
        public static readonly DirectProperty<Clipping, EaseProperty> BottomProperty = EditingProperty.RegisterDirect<EaseProperty, Clipping>(
            nameof(Bottom),
            owner => owner.Bottom,
            (owner, obj) => owner.Bottom = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Bottom, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Left"/> property.
        /// </summary>
        public static readonly DirectProperty<Clipping, EaseProperty> LeftProperty = EditingProperty.RegisterDirect<EaseProperty, Clipping>(
            nameof(Left),
            owner => owner.Left,
            (owner, obj) => owner.Left = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Left, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Right"/> property.
        /// </summary>
        public static readonly DirectProperty<Clipping, EaseProperty> RightProperty = EditingProperty.RegisterDirect<EaseProperty, Clipping>(
            nameof(Right),
            owner => owner.Right,
            (owner, obj) => owner.Right = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Right, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="AdjustCoordinates"/> property.
        /// </summary>
        public static readonly DirectProperty<Clipping, CheckProperty> AdjustCoordinatesProperty = EditingProperty.RegisterDirect<CheckProperty, Clipping>(
            nameof(AdjustCoordinates),
            owner => owner.AdjustCoordinates,
            (owner, obj) => owner.AdjustCoordinates = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.AdjustCoordinates)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipping"/> class.
        /// </summary>
        public Clipping()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Clipping;

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Top { get; private set; }

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Bottom { get; private set; }

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Left { get; private set; }

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Right { get; private set; }

        /// <summary>
        /// Gets whether or not to adjust the coordinates.
        /// </summary>
        [AllowNull]
        public CheckProperty AdjustCoordinates { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.Select(texture =>
            {
                using var image = texture.ToImage();
                var top = Top[args.Frame];
                var bottom = Bottom[args.Frame];
                var left = Left[args.Frame];
                var right = Right[args.Frame];

                if (AdjustCoordinates.Value)
                {
                    var transform = texture.Transform;
                    transform.Center += new Vector3(-(right / 2) + (left / 2), -(top / 2) + (bottom / 2), 0);
                    texture.Transform = transform;
                }

                if (image.Width <= left + right || image.Height <= top + bottom)
                {
                    using var empty = new Image<BGRA32>(1, 1, default(BGRA32));
                    texture.Update(empty);

                    return texture;
                }

                var width = image.Width - left - right;
                var height = image.Height - top - bottom;
                var x = left;
                var y = top;

                using var img1 = image[new Rectangle((int)x, (int)y, (int)width, (int)height)];
                texture.Update(img1);

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
            yield return Top;
            yield return Bottom;
            yield return Left;
            yield return Right;
            yield return AdjustCoordinates;
        }
    }
}