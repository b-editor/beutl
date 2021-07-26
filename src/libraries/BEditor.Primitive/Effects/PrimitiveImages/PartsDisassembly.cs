using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;

namespace BEditor.Primitive.Effects
{
    public sealed class PartsDisassembly : ImageEffect
    {
        /// <inheritdoc/>
        public override string Name => "パーツ分解";

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
        }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<IEnumerable<ImageInfo>> args)
        {
            args.Value = args.Value.SelectMany(i => Selector(i, args.Frame));
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield break;
        }

        private IEnumerable<ImageInfo> Selector(ImageInfo image, Frame frame)
        {
            var img = image.Source.PartsDisassembly();
            var size = image.Source.Size;

            foreach (var (part, rect) in img)
            {
                yield return new ImageInfo(part, _ =>
                {
                    var x = rect.X + rect.Width / 2 - size.Width / 2;
                    var y = rect.Y + rect.Height / 2 - size.Height / 2;
                    return new Transform(new(x, -y, 0), default, default, default);
                });
            }
        }
    }
}
