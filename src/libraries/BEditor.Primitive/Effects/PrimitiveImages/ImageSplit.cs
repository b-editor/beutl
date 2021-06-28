// ImageSplit.cs
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
    /// Represents the <see cref="ImageEffect"/> that splits an image.
    /// </summary>
    public sealed class ImageSplit : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="HSplit"/> property.
        /// </summary>
        public static readonly DirectProperty<ImageSplit, EaseProperty> HSplitProperty = EditingProperty.RegisterDirect<EaseProperty, ImageSplit>(
            nameof(HSplit),
            owner => owner.HSplit,
            (owner, obj) => owner.HSplit = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.NumberOfHorizontalDivisions, 2, min: 1)).Serialize());

        /// <summary>
        /// Defines the <see cref="VSplit"/> property.
        /// </summary>
        public static readonly DirectProperty<ImageSplit, EaseProperty> VSplitProperty = EditingProperty.RegisterDirect<EaseProperty, ImageSplit>(
            nameof(VSplit),
            owner => owner.VSplit,
            (owner, obj) => owner.VSplit = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.NumberOfVerticalDivisions, 2, min: 1)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageSplit"/> class.
        /// </summary>
        public ImageSplit()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ImageSplit;

        /// <summary>
        /// Gets the number of horizontal divisions.
        /// </summary>
        [AllowNull]
        public EaseProperty HSplit { get; private set; }

        /// <summary>
        /// Gets the number of vertical divisions.
        /// </summary>
        [AllowNull]
        public EaseProperty VSplit { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<ImageInfo>> args)
        {
            // forに使う変数はキャプチャされないのでこれで対策
            Func<ImageInfo, Transform> GetTransform(float x, float y, float hsplit, float vsplit)
            {
                return img =>
                {
                    var x_ = img.Source.Width * x;
                    var y_ = -img.Source.Height * y;

                    x_ -= ((hsplit / 2) * img.Source.Width) - (img.Source.Width / 2);
                    y_ += ((vsplit / 2) * img.Source.Height) - (img.Source.Height / 2);

                    var trans = new Transform(
                        new Vector3(x_, y_, 0),
                        Vector3.Zero,
                        Vector3.Zero,
                        Vector3.Zero);

                    return trans;
                };
            }

            args.Value = args.Value.SelectMany(img =>
            {
                var hsplt = HSplit[args.Frame];
                var vsplt = VSplit[args.Frame];
                var sw = img.Source.Width / hsplt;
                var sh = img.Source.Height / vsplt;
                var result = new ImageInfo[(int)(hsplt * vsplt)];
                var count = 0;

                for (var v = 0; v < vsplt; v++)
                {
                    for (var h = 0; h < hsplt; h++, count++)
                    {
                        result[count] = new(
                            img.Source[new Rectangle((int)(sw * h), (int)(sh * v), (int)sw, (int)sh)],
                            GetTransform(h, v, hsplt, vsplt));
                    }
                }

                return result;
            });
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return HSplit;
            yield return VSplit;
        }
    }
}