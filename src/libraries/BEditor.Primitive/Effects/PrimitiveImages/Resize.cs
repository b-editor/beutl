// Resize.cs
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
    /// The resize.
    /// </summary>
    public sealed class Resize : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Scale"/> property.
        /// </summary>
        public static readonly DirectProperty<Resize, EaseProperty> ScaleProperty = EditingProperty.RegisterDirect<EaseProperty, Resize>(
            nameof(Scale),
            owner => owner.Scale,
            (owner, obj) => owner.Scale = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Scale, 100, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="ScaleX"/> property.
        /// </summary>
        public static readonly DirectProperty<Resize, EaseProperty> ScaleXProperty = EditingProperty.RegisterDirect<EaseProperty, Resize>(
            nameof(ScaleX),
            owner => owner.ScaleX,
            (owner, obj) => owner.ScaleX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.X, 100, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="ScaleY"/> property.
        /// </summary>
        public static readonly DirectProperty<Resize, EaseProperty> ScaleYProperty = EditingProperty.RegisterDirect<EaseProperty, Resize>(
            nameof(ScaleY),
            owner => owner.ScaleY,
            (owner, obj) => owner.ScaleY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Y, 100, min: 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Quality"/> property.
        /// </summary>
        public static readonly DirectProperty<Resize, SelectorProperty> QualityProperty = EditingProperty.RegisterDirect<SelectorProperty, Resize>(
            nameof(Quality),
            owner => owner.Quality,
            (owner, obj) => owner.Quality = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.Quality, new string[] { "None", "Low", "Medium", "High" })).Serialize());

        /// <inheritdoc/>
        public override string Name => Strings.Resize;

        /// <summary>
        /// Gets the EaseProperty representing the scale.
        /// </summary>
        [AllowNull]
        public EaseProperty Scale { get; private set; }

        /// <summary>
        /// Gets the scale in the Z-axis direction.
        /// </summary>
        [AllowNull]
        public EaseProperty ScaleX { get; private set; }

        /// <summary>
        /// Gets the scale in the Y-axis direction.
        /// </summary>
        [AllowNull]
        public EaseProperty ScaleY { get; private set; }

        /// <summary>
        /// Gets the quality.
        /// </summary>
        [AllowNull]
        public SelectorProperty Quality { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var scale = Scale[args.Frame] / 100F;
            var scaleX = ScaleX[args.Frame] / 100F * scale;
            var scaleY = ScaleY[args.Frame] / 100F * scale;
            var width = (int)(args.Value.Width * scaleX);
            var height = (int)(args.Value.Height * scaleY);

            if (width <= 0 || height <= 0)
            {
                args.Value.Dispose();
                args.Value = new Image<BGRA32>(1, 1, default(BGRA32));
                return;
            }

            var tmp = args.Value.Resize(width, height, (Quality)Quality.Value);
            args.Value.Dispose();
            args.Value = tmp;
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Scale;
            yield return ScaleX;
            yield return ScaleY;
            yield return Quality;
        }
    }
}