// Sepia.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an effect that turns an image into a sepia tone.
    /// </summary>
    public sealed class Sepia : ImageEffect
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Sepia"/> class.
        /// </summary>
        public Sepia()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Sepia;

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.Sepia(Parent.Parent.DrawingContext);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            return Enumerable.Empty<PropertyElement>();
        }
    }
}