// Skew.cs
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
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.LangResources;
using BEditor.Media;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// The effect of skewing the image.
    /// </summary>
    public sealed class Skew : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Angle"/> property.
        /// </summary>
        public static readonly DirectProperty<Skew, EaseProperty> AngleProperty
            = EditingProperty.RegisterDirect<EaseProperty, Skew>(
                nameof(Angle),
                owner => owner.Angle,
                (owner, obj) => owner.Angle = obj,
                EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Angle, -15, 90, -90)).Serialize());

        /// <summary>
        /// Defines the <see cref="Direction"/> property.
        /// </summary>
        public static readonly DirectProperty<Skew, SelectorProperty> DirectionProperty
            = EditingProperty.RegisterDirect<SelectorProperty, Skew>(
                nameof(Direction),
                owner => owner.Direction,
                (owner, obj) => owner.Direction = obj,
                EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.Direction, new string[] { Strings.HorizontalDirection, Strings.VerticalDirection })).Serialize());

        /// <inheritdoc/>
        public override string Name => Strings.Skew;

        /// <summary>
        /// Gets the angle of inclination.
        /// </summary>
        [AllowNull]
        public EaseProperty Angle { get; private set; }

        /// <summary>
        /// Gets the direction of inclination.
        /// </summary>
        [AllowNull]
        public SelectorProperty Direction { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.Select(texture =>
            {
                if (Direction.Value == 0)
                {
                    return ApplyHorizontal(texture, args.Frame);
                }
                else
                {
                    return ApplyVertical(texture, args.Frame);
                }
            });
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Angle;
            yield return Direction;
        }

        private Texture ApplyHorizontal(Texture texture, Frame frame)
        {
            var x = texture.Height * MathF.Cos((Angle[frame] + 90) * MathF.PI / 180);
            var hx = x / 2;

            texture.Vertices[0].PosX -= hx;
            texture.Vertices[1].PosX += hx;
            texture.Vertices[2].PosX += hx;
            texture.Vertices[3].PosX -= hx;

            return texture;
        }

        private Texture ApplyVertical(Texture texture, Frame frame)
        {
            var y = texture.Width * MathF.Sin(Angle[frame] * MathF.PI / 180);
            var hy = y / 2;

            texture.Vertices[0].PosY -= hy;
            texture.Vertices[1].PosY -= hy;
            texture.Vertices[2].PosY += hy;
            texture.Vertices[3].PosY += hy;

            return texture;
        }
    }
}
