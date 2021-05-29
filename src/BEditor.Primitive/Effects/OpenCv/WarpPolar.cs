// WarpPolar.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Data.Property.PrimitiveGroup;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Objects;
using BEditor.Primitive.Resources;

using OpenCvSharp;

namespace BEditor.Primitive.Effects.OpenCv
{
    /// <summary>
    /// Represents an effect that transforms an image into polar coordinates.
    /// </summary>
    public sealed class WarpPolar : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="CenterX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<WarpPolar, EaseProperty> CenterXProperty = Coordinate.CenterXProperty.WithOwner<WarpPolar>(
            owner => owner.CenterX,
            (owner, obj) => owner.CenterX = obj);

        /// <summary>
        /// Defines the <see cref="CenterY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<WarpPolar, EaseProperty> CenterYProperty = Coordinate.CenterYProperty.WithOwner<WarpPolar>(
            owner => owner.CenterY,
            (owner, obj) => owner.CenterY = obj);

        /// <summary>
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<WarpPolar, EaseProperty> RadiusProperty = CircularGradient.RadiusProperty.WithOwner<WarpPolar>(
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj);

        /// <summary>
        /// Defines the <see cref="Width"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<WarpPolar, EaseProperty> WidthProperty = GL3DObject.WidthProperty.WithOwner<WarpPolar>(
            owner => owner.Width,
            (owner, obj) => owner.Width = obj);

        /// <summary>
        /// Defines the <see cref="Height"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<WarpPolar, EaseProperty> HeightProperty = GL3DObject.HeightProperty.WithOwner<WarpPolar>(
            owner => owner.Height,
            (owner, obj) => owner.Height = obj);

        /// <summary>
        /// Defines the <see cref="Mode"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<WarpPolar, SelectorProperty> ModeProperty = EditingProperty.RegisterDirect<SelectorProperty, WarpPolar>(
            nameof(Mode),
            owner => owner.Mode,
            (owner, obj) => owner.Mode = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata("モード", new string[]
            {
                "極座標への線形変換",
                "極座標からの逆変換",
                "対数極座標への線形変換",
                "極座標からの逆変換",
            })).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="WarpPolar"/> class.
        /// </summary>
        public WarpPolar()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.WarpPolar;

        /// <summary>
        /// Gets the X coordinate of the center.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterX { get; private set; }

        /// <summary>
        /// Gets the Y coordinate of the center.
        /// </summary>
        [AllowNull]
        public EaseProperty CenterY { get; private set; }

        /// <summary>
        /// Gets the radius.
        /// </summary>
        [AllowNull]
        public EaseProperty Radius { get; private set; }

        /// <summary>
        /// Gets the width.
        /// </summary>
        [AllowNull]
        public EaseProperty Width { get; private set; }

        /// <summary>
        /// Gets the height.
        /// </summary>
        [AllowNull]
        public EaseProperty Height { get; private set; }

        /// <summary>
        /// Gets the mode.
        /// </summary>
        [AllowNull]
        public SelectorProperty Mode { get; private set; }

        /// <inheritdoc/>
        public override unsafe void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var image = args.Value;
            var f = args.Frame;
            fixed (BGRA32* ptr = image.Data)
            {
                using var src = new Mat(image.Height, image.Width, MatType.CV_8UC4, (IntPtr)ptr);
                using var dst = new Mat((int)Width[f], (int)Height[f], MatType.CV_8UC4, default);

                var interpolation = GetInterpolationFlags();
                var polarMode = GetWarpPolarMode();

                Cv2.WarpPolar(
                    src,
                    dst,
                    new(dst.Width, dst.Height),
                    new(CenterX[f] + (dst.Width / 2), CenterY[f] + (dst.Height / 2)),
                    Radius[f],
                    interpolation,
                    polarMode);

                args.Value = new(dst.Width, dst.Height);
                fixed (BGRA32* dstptr = args.Value.Data)
                {
                    Buffer.MemoryCopy((void*)dst.Data, dstptr, args.Value.DataSize, args.Value.DataSize);
                }
            }

            image.Dispose();
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return CenterX;
            yield return CenterY;
            yield return Radius;
            yield return Width;
            yield return Height;
            yield return Mode;
        }

        private InterpolationFlags GetInterpolationFlags()
        {
            return Mode.Index switch
            {
                0 => InterpolationFlags.Cubic | InterpolationFlags.WarpFillOutliers,
                1 => InterpolationFlags.Cubic | InterpolationFlags.WarpFillOutliers | InterpolationFlags.WarpInverseMap,
                2 => InterpolationFlags.Cubic | InterpolationFlags.WarpFillOutliers,
                3 => InterpolationFlags.Cubic | InterpolationFlags.WarpFillOutliers | InterpolationFlags.WarpInverseMap,
                _ => default,
            };
        }

        private WarpPolarMode GetWarpPolarMode()
        {
            return Mode.Index switch
            {
                0 => WarpPolarMode.Linear,
                1 => WarpPolarMode.Linear,
                2 => WarpPolarMode.Log,
                3 => WarpPolarMode.Log,
                _ => default,
            };
        }
    }
}