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
    /// Represents the <see cref="ImageEffect"/> that cripping the image.
    /// </summary>
    public sealed class Clipping : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Top"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Clipping, EaseProperty> TopProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Clipping>(
            nameof(Top),
            owner => owner.Top,
            (owner, obj) => owner.Top = obj,
            new EasePropertyMetadata(Strings.Top, 0, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="Bottom"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Clipping, EaseProperty> BottomProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Clipping>(
            nameof(Bottom),
            owner => owner.Bottom,
            (owner, obj) => owner.Bottom = obj,
            new EasePropertyMetadata(Strings.Bottom, 0, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="Left"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Clipping, EaseProperty> LeftProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Clipping>(
            nameof(Left),
            owner => owner.Left,
            (owner, obj) => owner.Left = obj,
            new EasePropertyMetadata(Strings.Left, 0, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="Right"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Clipping, EaseProperty> RightProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Clipping>(
            nameof(Right),
            owner => owner.Right,
            (owner, obj) => owner.Right = obj,
            new EasePropertyMetadata(Strings.Right, 0, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="AdjustCoordinates"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Clipping, CheckProperty> AdjustCoordinatesProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, Clipping>(
            nameof(AdjustCoordinates),
            owner => owner.AdjustCoordinates,
            (owner, obj) => owner.AdjustCoordinates = obj,
            new CheckPropertyMetadata(Strings.AdjustCoordinates));

        /// <summary>
        /// Initializes a new instance of the <see cref="Clipping"/> class.
        /// </summary>
        public Clipping()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Clipping;

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Top { get; private set; }

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Bottom { get; private set; }

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Left { get; private set; }

        /// <summary>
        /// Gets the range to be clipped.
        /// </summary>
        [AllowNull]
        public EaseProperty Right { get; private set; }

        /// <summary>
        /// Gets whether or not to adjust the coordinates.
        /// </summary>
        [AllowNull]
        public CheckProperty AdjustCoordinates { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var top = (int)Top.GetValue(args.Frame);
            var bottom = (int)Bottom.GetValue(args.Frame);
            var left = (int)Left.GetValue(args.Frame);
            var right = (int)Right.GetValue(args.Frame);
            var img = args.Value;

            if (AdjustCoordinates.Value && Parent!.Effect[0] is ImageObject image)
            {
                image.Coordinate.CenterX.Optional += -(right / 2) + (left / 2);
                image.Coordinate.CenterY.Optional += -(top / 2) + (bottom / 2);
            }

            if (img.Width <= left + right || img.Height <= top + bottom)
            {
                img.Dispose();
                args.Value = new(1, 1, default(BGRA32));
                return;
            }

            int width = img.Width - left - right;
            int height = img.Height - top - bottom;
            int x = left;
            int y = top;

            var img1 = img[new Rectangle(x, y, width, height)];
            img.Dispose();

            args.Value = img1;
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Top;
            yield return Bottom;
            yield return Left;
            yield return Right;
            yield return AdjustCoordinates;
        }
    }
}