// AreaExpansion.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
    /// Represents the <see cref="ImageEffect"/> that expands the area of an image.
    /// </summary>
    public sealed class AreaExpansion : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Top"/> property.
        /// </summary>
        public static readonly DirectProperty<AreaExpansion, EaseProperty> TopProperty = Clipping.TopProperty.WithOwner<AreaExpansion>(
            owner => owner.Top,
            (owner, obj) => owner.Top = obj);

        /// <summary>
        /// Defines the <see cref="Bottom"/> property.
        /// </summary>
        public static readonly DirectProperty<AreaExpansion, EaseProperty> BottomProperty = Clipping.BottomProperty.WithOwner<AreaExpansion>(
            owner => owner.Bottom,
            (owner, obj) => owner.Bottom = obj);

        /// <summary>
        /// Defines the <see cref="Left"/> property.
        /// </summary>
        public static readonly DirectProperty<AreaExpansion, EaseProperty> LeftProperty = Clipping.LeftProperty.WithOwner<AreaExpansion>(
            owner => owner.Left,
            (owner, obj) => owner.Left = obj);

        /// <summary>
        /// Defines the <see cref="Right"/> property.
        /// </summary>
        public static readonly DirectProperty<AreaExpansion, EaseProperty> RightProperty = Clipping.RightProperty.WithOwner<AreaExpansion>(
            owner => owner.Right,
            (owner, obj) => owner.Right = obj);

        /// <summary>
        /// Defines the <see cref="AdjustCoordinates"/> property.
        /// </summary>
        public static readonly DirectProperty<AreaExpansion, CheckProperty> AdjustCoordinatesProperty = Clipping.AdjustCoordinatesProperty.WithOwner<AreaExpansion>(
            owner => owner.AdjustCoordinates,
            (owner, obj) => owner.AdjustCoordinates = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="AreaExpansion"/> class.
        /// </summary>
        public AreaExpansion()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.AreaExpansion;

        /// <summary>
        /// Gets the number of pixels to add.
        /// </summary>
        [AllowNull]
        public EaseProperty Top { get; private set; }

        /// <summary>
        /// Gets the number of pixels to add.
        /// </summary>
        [AllowNull]
        public EaseProperty Bottom { get; private set; }

        /// <summary>
        /// Gets the number of pixels to add.
        /// </summary>
        [AllowNull]
        public EaseProperty Left { get; private set; }

        /// <summary>
        /// Gets the number of pixels to add.
        /// </summary>
        [AllowNull]
        public EaseProperty Right { get; private set; }

        /// <summary>
        /// Gets the <see cref="CheckProperty"/> to adjust the coordinates.
        /// </summary>
        [AllowNull]
        public CheckProperty AdjustCoordinates { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Top;
            yield return Bottom;
            yield return Left;
            yield return Right;
            yield return AdjustCoordinates;
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.Select(texture =>
            {
                using var image = texture.ToImage();
                var top = Top[args.Frame];
                var bottom = Bottom[args.Frame];
                var left = Left[args.Frame];
                var right = Right[args.Frame];

                if (AdjustCoordinates.Value)
                {
                    var transform = texture.Transform;
                    transform.Center += new Vector3((right / 2) - (left / 2), (top / 2) - (bottom / 2), 0);
                    texture.Transform = transform;
                }

                using var img = image.MakeBorder((int)top, (int)bottom, (int)left, (int)right);
                texture.Update(img);
                return texture;
            });
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }
    }
}