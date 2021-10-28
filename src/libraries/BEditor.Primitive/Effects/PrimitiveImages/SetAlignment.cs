// SetAlignment.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.LangResources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that sets the alignment.
    /// </summary>
    public sealed class SetAlignment : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="HorizontalAlign"/> property.
        /// </summary>
        public static readonly DirectProperty<SetAlignment, SelectorProperty> HorizontalAlignProperty = EditingProperty.RegisterDirect<SelectorProperty, SetAlignment>(
            nameof(HorizontalAlign),
            owner => owner.HorizontalAlign,
            (owner, obj) => owner.HorizontalAlign = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.HorizontalAlignment, new[]
            {
                Strings.Left,
                Strings.Center,
                Strings.Right,
            })).Serialize());

        /// <summary>
        /// Defines the <see cref="VerticalAlign"/> property.
        /// </summary>
        public static readonly DirectProperty<SetAlignment, SelectorProperty> VerticalAlignProperty = EditingProperty.RegisterDirect<SelectorProperty, SetAlignment>(
            nameof(VerticalAlign),
            owner => owner.VerticalAlign,
            (owner, obj) => owner.VerticalAlign = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.VerticalAlignment, new[]
            {
                Strings.Top,
                Strings.Center,
                Strings.Bottom,
            })).Serialize());

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
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.Select(texture =>
            {
                var center = texture.Transform.Center;
                if (HorizontalAlign.Index is 0)
                {
                    center.X += texture.Width / 2;
                }
                else if (HorizontalAlign.Index is 2)
                {
                    center.X -= texture.Width / 2;
                }

                if (VerticalAlign.Index is 0)
                {
                    center.Y -= texture.Height / 2;
                }
                else if (VerticalAlign.Index is 2)
                {
                    center.Y += texture.Height / 2;
                }

                var transform = texture.Transform;
                transform.Center = center;
                texture.Transform = transform;

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
            yield return HorizontalAlign;
            yield return VerticalAlign;
        }
    }
}