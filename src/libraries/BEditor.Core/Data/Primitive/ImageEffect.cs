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
        public virtual void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.Select(tex =>
            {
                var innerArgs = new EffectApplyArgs<Image<BGRA32>>(args.Frame, tex.ToImage(), args.Type);

                // エフェクトを適用
                Apply(innerArgs);

                // テクスチャを更新
                tex.Update(innerArgs.Value);
                innerArgs.Value.Dispose();

                args.Handled = innerArgs.Handled;

                return tex;
            });
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
        }
    }
}