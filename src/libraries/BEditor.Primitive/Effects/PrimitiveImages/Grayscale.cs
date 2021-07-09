// Grayscale.cs
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
    /// Represents the <see cref="ImageEffect"/> of grayscaling an image.
    /// </summary>
    public sealed class Grayscale : ImageEffect
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Grayscale"/> class.
        /// </summary>
        public Grayscale()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Grayscale;

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.Grayscale(Parent.Parent.DrawingContext);

            // var mat = new ColorMatrix(
            //     0.3086f, 0.3086f, 0.3086f, 0, 0,
            //     0.6094f, 0.6094f, 0.6094f, 0, 0,
            //     0.0820f, 0.0820f, 0.0820f, 0, 0,
            //     0, 0, 0, 1, 0,
            //     0, 0, 0, 0, 1);
            // args.Value.Apply(ref mat, Parent.Parent.DrawingContext);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            return Enumerable.Empty<PropertyElement>();
        }
    }
}