// Solarisation.cs
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
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects.LookupTables
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that do the solarization process.
    /// </summary>
    public sealed class Solarisation : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Cycle"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Solarisation, EaseProperty> CycleProperty = EditingProperty.RegisterDirect<EaseProperty, Solarisation>(
            nameof(Cycle),
            owner => owner.Cycle,
            (owner, obj) => owner.Cycle = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Cycle, 2, 100, 1)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="Solarisation"/> class.
        /// </summary>
        public Solarisation()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Solarisation;

        /// <summary>
        /// Gets the cycle.
        /// </summary>
        [AllowNull]
        public EaseProperty Cycle { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            using var lut = Drawing.LookupTable.Solarisation((int)Cycle[args.Frame]);
            args.Value.Apply(lut);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Cycle;
        }
    }
}