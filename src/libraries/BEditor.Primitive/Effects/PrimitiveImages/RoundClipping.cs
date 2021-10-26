// RoundClipping.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
using BEditor.LangResources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that cripping the image.
    /// </summary>
    public sealed class RoundClipping : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Top"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> TopProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(Top),
            owner => owner.Top,
            (owner, obj) => owner.Top = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Top, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Bottom"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> BottomProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(Bottom),
            owner => owner.Bottom,
            (owner, obj) => owner.Bottom = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Bottom, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Left"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> LeftProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(Left),
            owner => owner.Left,
            (owner, obj) => owner.Left = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Left, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Right"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> RightProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(Right),
            owner => owner.Right,
            (owner, obj) => owner.Right = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Right, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="TopLeftRadius"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> TopLeftRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(TopLeftRadius),
            owner => owner.TopLeftRadius,
            (owner, obj) => owner.TopLeftRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.TopLeft} ({Strings.Radius})", 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="TopRightRadius"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> TopRightRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(TopRightRadius),
            owner => owner.TopRightRadius,
            (owner, obj) => owner.TopRightRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.TopRight} ({Strings.Radius})", 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="BottomLeftRadius"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> BottomLeftRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(BottomLeftRadius),
            owner => owner.BottomLeftRadius,
            (owner, obj) => owner.BottomLeftRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.BottomLeft} ({Strings.Radius})", 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="BottomRightRadius"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, EaseProperty> BottomRightRadiusProperty = EditingProperty.RegisterDirect<EaseProperty, RoundClipping>(
            nameof(BottomRightRadius),
            owner => owner.BottomRightRadius,
            (owner, obj) => owner.BottomRightRadius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata($"{Strings.BottomRight} ({Strings.Radius})", 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="AdjustCoordinates"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, CheckProperty> AdjustCoordinatesProperty = EditingProperty.RegisterDirect<CheckProperty, RoundClipping>(
            nameof(AdjustCoordinates),
            owner => owner.AdjustCoordinates,
            (owner, obj) => owner.AdjustCoordinates = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.AdjustCoordinates)).Serialize());

        /// <summary>
        /// Defines the <see cref="CropTransparentArea"/> property.
        /// </summary>
        public static readonly DirectProperty<RoundClipping, CheckProperty> CropTransparentAreaProperty = EditingProperty.RegisterDirect<CheckProperty, RoundClipping>(
            nameof(CropTransparentArea),
            owner => owner.CropTransparentArea,
            (owner, obj) => owner.CropTransparentArea = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.CropTransparentArea)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="RoundClipping"/> class.
        /// </summary>
        public RoundClipping()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.RoundClipping;

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
        /// Gets the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty TopLeftRadius { get; private set; }

        /// <summary>
        /// Gets the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty TopRightRadius { get; private set; }

        /// <summary>
        /// Gets the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty BottomLeftRadius { get; private set; }

        /// <summary>
        /// Gets the roundness of the shape.
        /// </summary>
        [AllowNull]
        public EaseProperty BottomRightRadius { get; private set; }

        /// <summary>
        /// Gets whether or not to adjust the coordinates.
        /// </summary>
        [AllowNull]
        public CheckProperty AdjustCoordinates { get; private set; }

        /// <summary>
        /// Gets whether or not to crop the transparent area.
        /// </summary>
        [AllowNull]
        public CheckProperty CropTransparentArea { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.Select(texture =>
            {
                using var image = texture.ToImage();
                var rect = GetRect(image, args.Frame);
                using var roundrect = Image.RoundRect(
                    rect.Width,
                    rect.Height,
                    Math.Max(rect.Width, rect.Height),
                    Colors.White,
                    (int)TopLeftRadius[args.Frame],
                    (int)TopRightRadius[args.Frame],
                    (int)BottomLeftRadius[args.Frame],
                    (int)BottomRightRadius[args.Frame]);

                if (rect.Width < 0 || rect.Height < 0)
                {
                    using var empty = new Image<BGRA32>(1, 1, default(BGRA32));
                    texture.Update(empty);

                    return texture;
                }

                if (AdjustCoordinates.Value)
                {
                    var transform = texture.Transform;

                    transform.Center += new Vector3(
                        (rect.X / 2F) - ((image.Width - rect.Right) / 2F),
                        ((image.Height - rect.Bottom) / 2F) - (rect.Y / 2F),
                        0);
                    texture.Transform = transform;
                }

                using var img1 = image[rect];
                img1.Mask(roundrect, default, 0, false, args.Contexts.Drawing);

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
            yield return TopLeftRadius;
            yield return TopRightRadius;
            yield return BottomLeftRadius;
            yield return BottomRightRadius;
            yield return AdjustCoordinates;
            yield return CropTransparentArea;
        }

        private static unsafe Rectangle FindRect(Image<BGRA32> image)
        {
            var x0 = image.Width;
            var y0 = image.Height;
            var x1 = 0;
            var y1 = 0;

            // 透明でないピクセルを探す
            Parallel.For(0, image.Data.Length, i =>
            {
                if (image.Data[i].A != 0)
                {
                    var x = i % image.Width;
                    var y = i / image.Width;

                    if (x0 > x) x0 = x;
                    if (y0 > y) y0 = y;
                    if (x1 < x) x1 = x;
                    if (y1 < y) y1 = y;
                }
            });

            return new Rectangle(x0, y0, x1 - x0, y1 - y0);
        }

        private Rectangle GetRect(Image<BGRA32> image, Frame frame)
        {
            if (CropTransparentArea.Value)
            {
                return FindRect(image);
            }
            else
            {
                var top = Top[frame];
                var bottom = Bottom[frame];
                var left = Left[frame];
                var right = Right[frame];

                var width = image.Width - left - right;
                var height = image.Height - top - bottom;
                var x = left;
                var y = top;

                return new Rectangle((int)x, (int)y, (int)width, (int)height);
            }
        }
    }
}