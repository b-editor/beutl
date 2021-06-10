// Erode.cs
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
    /// Represents the <see cref="ImageEffect"/> that erodes an image.
    /// </summary>
    public sealed class Erode : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Erode, EaseProperty> RadiusProperty = Dilate.RadiusProperty.WithOwner<Erode>(
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj);

        /// <summary>
        /// Defines the <see cref="Resize"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Erode, CheckProperty> ResizeProperty = Dilate.ResizeProperty.WithOwner<Erode>(
            owner => owner.Resize,
            (owner, obj) => owner.Resize = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="Erode"/> class.
        /// </summary>
        public Erode()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Erode;

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the radius.
        /// </summary>
        [AllowNull]
        public EaseProperty Radius { get; private set; }

        /// <summary>
        /// Gets a <see cref="CheckProperty"/> representing the value to resize the image.
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
                // Todo: 画像をリサイズ
                args.Value.Erode(size);

                img.Dispose();
            }
            else
            {
                img.Erode(size);
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