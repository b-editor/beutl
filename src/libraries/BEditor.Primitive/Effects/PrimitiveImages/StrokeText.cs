// StrokeText.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    [Obsolete("Use Text.Stroke")]
    public sealed class StrokeText : ImageEffect
    {
        /// <inheritdoc/>
        public override string Name => $"{Strings.Border} ({Strings.Text})";

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield break;
        }
    }
}