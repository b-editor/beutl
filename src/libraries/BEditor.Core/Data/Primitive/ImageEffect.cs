// ImageEffect.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Linq;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Data.Primitive
{
    /// <summary>
    /// Represents an effect that can be added to an <see cref="ImageObject"/>.
    /// </summary>
    public abstract class ImageEffect : EffectElement
    {
        /// <inheritdoc cref="Apply(EffectApplyArgs{Image{BGRA32}})"/>
        public abstract void Apply(EffectApplyArgs<Image<BGRA32>> args);

        /// <inheritdoc cref="Apply(EffectApplyArgs{Image{BGRA32}})"/>
        public virtual void Apply(EffectApplyArgs<IEnumerable<ImageInfo>> args)
        {
            args.Value = args.Value.Select(img =>
            {
                var a = new EffectApplyArgs<Image<BGRA32>>(args.Frame, img.Source, args.Type);
                Apply(a);
                img.Source = a.Value;
                args.Handled = a.Handled;

                return img;
            });
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
        }
    }
}