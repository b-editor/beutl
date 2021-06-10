// SetAlignment.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Objects;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that sets the alignment.
    /// </summary>
    public sealed class SetAlignment : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="VerticalAlign"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<SetAlignment, SelectorProperty> VerticalAlignProperty = Text.VerticalAlignProperty.WithOwner<SetAlignment>(
            owner => owner.VerticalAlign,
            (owner, obj) => owner.VerticalAlign = obj);

        /// <summary>
        /// Defines the <see cref="HorizontalAlign"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<SetAlignment, SelectorProperty> HorizontalAlignProperty = Text.HorizontalAlignProperty.WithOwner<SetAlignment>(
            owner => owner.HorizontalAlign,
            (owner, obj) => owner.HorizontalAlign = obj);

        /// <inheritdoc/>
        public override string Name => Strings.SetAlignment;

        /// <summary>
        /// Gets the horizontal alignment.
        /// </summary>
        [AllowNull]
        public SelectorProperty HorizontalAlign { get; private set; }

        /// <summary>
        /// Gets the vertical alignment.
        /// </summary>
        [AllowNull]
        public SelectorProperty VerticalAlign { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is not ImageObject obj) return;

            var img = args.Value;

            if (HorizontalAlign.Index is 0)
            {
                obj.Coordinate.CenterX.Optional = img.Width / 2;
            }
            else if (HorizontalAlign.Index is 2)
            {
                obj.Coordinate.CenterX.Optional = -img.Width / 2;
            }

            if (VerticalAlign.Index is 0)
            {
                obj.Coordinate.CenterY.Optional = -img.Height / 2;
            }
            else if (VerticalAlign.Index is 2)
            {
                obj.Coordinate.CenterY.Optional = img.Height / 2;
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return HorizontalAlign;
            yield return VerticalAlign;
        }
    }
}