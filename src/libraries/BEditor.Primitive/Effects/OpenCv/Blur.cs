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
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Blur, EaseProperty> SizeProperty = EditingProperty.RegisterDirect<EaseProperty, Blur>(
            nameof(Size),
            owner => owner.Size,
            (owner, obj) => owner.Size = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Size, 20, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Resize"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Blur, CheckProperty> ResizeProperty = EditingProperty.RegisterDirect<CheckProperty, Blur>(
            nameof(Resize),
            owner => owner.Resize,
            (owner, obj) => owner.Resize = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.Resize, true)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Blur"/> class.
        /// </summary>
        public Blur()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Blur;

        /// <summary>
        /// Gets the size of the kernel.
        /// </summary>
        [AllowNull]
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the value if the image should be resized.
        /// </summary>
        [AllowNull]
        public CheckProperty Resize { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var size = (int)Size[args.Frame];
            if (Resize.Value)
            {
                var image = args.Value.MakeBorder(args.Value.Width + size, args.Value.Height + size);
                args.Value.Dispose();
                args.Value = image;
            }

            Cv.Blur(args.Value, size);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Size;
            yield return Resize;
        }
    }
}