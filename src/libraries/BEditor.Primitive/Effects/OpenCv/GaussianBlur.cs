// GaussianBlur.cs
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
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects.OpenCv
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that blurs the image.
    /// </summary>
    public sealed class GaussianBlur : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="KernelWidth"/> property.
        /// </summary>
        public static readonly DirectProperty<GaussianBlur, EaseProperty> KernelWidthProperty = EditingProperty.RegisterDirect<EaseProperty, GaussianBlur>(
            $"{nameof(KernelWidth)},Size",
            owner => owner.KernelWidth,
            (owner, obj) => owner.KernelWidth = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.KernelWidth, 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="KernelHeight"/> property.
        /// </summary>
        public static readonly DirectProperty<GaussianBlur, EaseProperty> KernelHeightProperty = EditingProperty.RegisterDirect<EaseProperty, GaussianBlur>(
            nameof(KernelHeight),
            owner => owner.KernelHeight,
            (owner, obj) => owner.KernelHeight = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.KernelHeight, 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="SigmaX"/> property.
        /// </summary>
        public static readonly DirectProperty<GaussianBlur, EaseProperty> SigmaXProperty = Effects.Blur.SigmaXProperty.WithOwner<GaussianBlur>(
            owner => owner.SigmaX,
            (owner, obj) => owner.SigmaX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.SigmaX, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="SigmaY"/> property.
        /// </summary>
        public static readonly DirectProperty<GaussianBlur, EaseProperty> SigmaYProperty = Effects.Blur.SigmaYProperty.WithOwner<GaussianBlur>(
            owner => owner.SigmaY,
            (owner, obj) => owner.SigmaY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.SigmaY, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="FixSize"/> property.
        /// </summary>
        public static readonly DirectProperty<GaussianBlur, CheckProperty> FixSizeProperty = Blur.FixSizeProperty.WithOwner<GaussianBlur>(
            owner => owner.FixSize,
            (owner, obj) => owner.FixSize = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="GaussianBlur"/> class.
        /// </summary>
        public GaussianBlur()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.GaussianBlur;

        /// <summary>
        /// Gets the width of the kernel.
        /// </summary>
        [AllowNull]
        public EaseProperty KernelWidth { get; private set; }

        /// <summary>
        /// Gets the height of the kernel.
        /// </summary>
        [AllowNull]
        public EaseProperty KernelHeight { get; private set; }

        /// <summary>
        /// Gets the blur sigma.
        /// </summary>
        [AllowNull]
        public EaseProperty SigmaX { get; private set; }

        /// <summary>
        /// Gets the blur sigma.
        /// </summary>
        [AllowNull]
        public EaseProperty SigmaY { get; private set; }

        /// <summary>
        /// Gets whether the size should be fixed.
        /// </summary>
        [AllowNull]
        public CheckProperty FixSize { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var width = (int)KernelWidth[args.Frame];
            var height = (int)KernelHeight[args.Frame];
            var sigmaX = SigmaX[args.Frame];
            var sigmaY = SigmaY[args.Frame];

            if (!FixSize.Value)
            {
                var image = args.Value.MakeBorder(
                    args.Value.Width + width,
                    args.Value.Height + height);
                args.Value.Dispose();
                args.Value = image;
            }

            Cv.GaussianBlur(args.Value, new(width, height), sigmaX, sigmaY);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return KernelWidth;
            yield return KernelHeight;
            yield return SigmaX;
            yield return SigmaY;
            yield return FixSize;
        }
    }
}