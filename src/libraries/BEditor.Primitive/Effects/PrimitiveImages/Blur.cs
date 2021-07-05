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
using BEditor.Primitive.Effects.OpenCv;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that blurs the image.
    /// </summary>
    public sealed class Blur : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="SigmaX"/> property.
        /// </summary>
        public static readonly DirectProperty<Blur, EaseProperty> SigmaXProperty = EditingProperty.RegisterDirect<EaseProperty, Blur>(
            nameof(SigmaX),
            owner => owner.SigmaX,
            (owner, obj) => owner.SigmaX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.SigmaX, 25, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="SigmaX"/> property.
        /// </summary>
        public static readonly DirectProperty<Blur, EaseProperty> SigmaYProperty = EditingProperty.RegisterDirect<EaseProperty, Blur>(
            nameof(SigmaY),
            owner => owner.SigmaY,
            (owner, obj) => owner.SigmaY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.SigmaY, 25, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="FixSize"/> property.
        /// </summary>
        public static readonly DirectProperty<Blur, CheckProperty> FixSizeProperty = OpenCv.Blur.FixSizeProperty.WithOwner<Blur>(
            owner => owner.FixSize,
            (owner, obj) => owner.FixSize = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="Blur"/> class.
        /// </summary>
        public Blur()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Blur;

        /// <summary>
        /// Gets the blur sigma.
        /// </summary>
        [AllowNull]
        public EaseProperty SigmaX { get; private set; }

        /// <summary>
        /// Gets the blur sigma.
        /// </summary>
        [AllowNull]
        public EaseProperty SigmaY { get; private set; }

        /// <summary>
        /// Gets whether the size should be fixed.
        /// </summary>
        [AllowNull]
        public CheckProperty FixSize { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var sigmaX = SigmaX.GetValue(args.Frame);
            var sigmaY = SigmaY.GetValue(args.Frame);
            if (sigmaX is 0 && sigmaY is 0) return;

            if (!FixSize.Value)
            {
                var image = args.Value.MakeBorder((int)(args.Value.Width + sigmaX), (int)(args.Value.Height + sigmaY));
                args.Value.Dispose();
                args.Value = image;
            }

            args.Value.Blur(sigmaX, sigmaY);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return SigmaX;
            yield return SigmaY;
            yield return FixSize;
        }
    }
}