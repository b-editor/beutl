// Blur.cs
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
    public sealed class Blur : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="KernelWidth"/> property.
        /// </summary>
        public static readonly DirectProperty<Blur, EaseProperty> KernelWidthProperty = GaussianBlur.KernelWidthProperty.WithOwner<Blur>(
            owner => owner.KernelWidth,
            (owner, obj) => owner.KernelWidth = obj);

        /// <summary>
        /// Defines the <see cref="KernelHeight"/> property.
        /// </summary>
        public static readonly DirectProperty<Blur, EaseProperty> KernelHeightProperty = GaussianBlur.KernelHeightProperty.WithOwner<Blur>(
            owner => owner.KernelHeight,
            (owner, obj) => owner.KernelHeight = obj);

        /// <summary>
        /// Defines the <see cref="FixSize"/> property.
        /// </summary>
        public static readonly DirectProperty<Blur, CheckProperty> FixSizeProperty = GaussianBlur.FixSizeProperty.WithOwner<Blur>(
            owner => owner.FixSize,
            (owner, obj) => owner.FixSize = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="Blur"/> class.
        /// </summary>
        public Blur()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Blur;

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
        /// Gets whether the size should be fixed.
        /// </summary>
        [AllowNull]
        public CheckProperty FixSize { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var width = (int)KernelWidth[args.Frame];
            var height = (int)KernelHeight[args.Frame];

            if (!FixSize.Value)
            {
                var image = args.Value.MakeBorder(args.Value.Width + width, args.Value.Height + height);
                args.Value.Dispose();
                args.Value = image;
            }

            Cv.Blur(args.Value, new Size(width, height));
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return KernelWidth;
            yield return KernelHeight;
            yield return FixSize;
        }
    }
}