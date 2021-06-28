// Dilate.cs
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

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that dilates an image.
    /// </summary>
    public sealed class Dilate : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectProperty<Dilate, EaseProperty> RadiusProperty = EditingProperty.RegisterDirect<EaseProperty, Dilate>(
            nameof(Radius),
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Radius, 1, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Resize"/> property.
        /// </summary>
        public static readonly DirectProperty<Dilate, CheckProperty> ResizeProperty = EditingProperty.RegisterDirect<CheckProperty, Dilate>(
            nameof(Resize),
            owner => owner.Resize,
            (owner, obj) => owner.Resize = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.Resize)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Dilate"/> class.
        /// </summary>
        public Dilate()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Dilate;

        /// <summary>
        /// Gets the radius.
        /// </summary>
        [AllowNull]
        public EaseProperty Radius { get; private set; }

        /// <summary>
        /// Gets the value to resize the image.
        /// </summary>
        [AllowNull]
        public CheckProperty Resize { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var img = args.Value;
            var size = (int)Radius.GetValue(args.Frame);
            if (Resize.Value)
            {
                var nwidth = img.Width + ((size + 5) * 2);
                var nheight = img.Height + ((size + 5) * 2);

                args.Value = img.MakeBorder(nwidth, nheight);
                args.Value.Dilate(size);

                img.Dispose();
            }
            else
            {
                img.Dilate(size);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Radius;
            yield return Resize;
        }
    }
}