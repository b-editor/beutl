// GammaCorrection.cs
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

namespace BEditor.Primitive.Effects.LookupTables
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that corrects the gamma of an image.
    /// </summary>
    public sealed class GammaCorrection : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Gamma"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<GammaCorrection, EaseProperty> GammaProperty = EditingProperty.RegisterDirect<EaseProperty, GammaCorrection>(
            nameof(Gamma),
            owner => owner.Gamma,
            (owner, obj) => owner.Gamma = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Gamma, 100, 300, 1)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="GammaCorrection"/> class.
        /// </summary>
        public GammaCorrection()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.GammaCorrection;

        /// <summary>
        /// Gets the gamma.
        /// </summary>
        [AllowNull]
        public EaseProperty Gamma { get; set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            using var lut = Drawing.LookupTable.Gamma(Gamma[args.Frame] / 100);
            args.Value.Apply(lut);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Gamma;
        }
    }
}