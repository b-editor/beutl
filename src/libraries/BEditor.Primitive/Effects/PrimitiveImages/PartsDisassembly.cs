// PartsDisassembly.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// This effect decomposes the pearl in an image.
    /// </summary>
    public sealed class PartsDisassembly : ImageEffect
    {
        /// <inheritdoc/>
        public override string Name => Strings.PartsDisassembly;

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<Texture>> args)
        {
            args.Value = args.Value.SelectMany(i => Selector(i));
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield break;
        }

        private static IEnumerable<Texture> Selector(Texture texture)
        {
            var image = texture.ToImage();
            var images = image.PartsDisassembly();
            var size = image.Size;
            image.Dispose();

            foreach (var (part, rect) in images)
            {
                // テクスチャを複製
                var item = Texture.FromImage(part);
                item.Synchronize(texture);

                // Transform設定
                var x = rect.X + (rect.Width / 2) - (size.Width / 2);
                var y = rect.Y + (rect.Height / 2) - (size.Height / 2);
                var transform = item.Transform;
                transform.Coordinate += new Vector3(x, -y, 0);
                item.Transform = transform;

                part.Dispose();
                yield return item;
            }

            texture.Dispose();
        }
    }
}
