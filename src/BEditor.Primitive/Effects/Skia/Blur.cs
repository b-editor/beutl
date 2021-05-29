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
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that blurs the image.
    /// </summary>
    public sealed class Blur : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Blur, EaseProperty> TopProperty = EditingProperty.RegisterDirect<EaseProperty, Blur>(
            nameof(Size),
            owner => owner.Size,
            (owner, obj) => owner.Size = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Size, 25, float.NaN, 0)).Serialize());

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
        public EaseProperty Size { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            var size = (int)Size.GetValue(args.Frame);
            if (size is 0) return;

            args.Value.Blur(size);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Size;
        }
    }
}