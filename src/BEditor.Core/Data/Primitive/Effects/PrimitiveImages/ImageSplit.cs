using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Core.Data.Primitive.Effects
{
    /// <summary>
    /// Represents a <see cref="MultipleImageEffect"/> that splits an image.
    /// </summary>
    public class ImageSplit : MultipleImageEffect
    {
        /// <summary>
        /// Represents <see cref="HSplit"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata HSplitMetadata = new(Resources.NumberOfHorizontalDivisions, 2, Min: 1);
        /// <summary>
        /// Represents <see cref="VSplit"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata VSplitMetadata = new(Resources.NumberOfVerticalDivisions, 2, Min: 1);

        /// <summary>
        /// Initializes a new instance of the <see cref="ImageSplit"/> class.
        /// </summary>
        public ImageSplit()
        {
            HSplit = new(HSplitMetadata);
            VSplit = new(VSplitMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.ImageSplit;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            HSplit,
            VSplit,
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the number of horizontal divisions.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty HSplit { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the number of vertical divisions.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty VSplit { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs<Image<BGRA32>> args)
        {
            //forに使う変数はキャプチャされないのでこれで対策
            Func<ImageInfo, Transform> GetTransform(float x, float y, float hsplit, float vsplit)
            {
                return img =>
                {
                    var _ = hsplit;
                    var __ = vsplit;

                    var x_ = img.Source.Width * x;
                    var y_ = -img.Source.Height * y;

                    x_ -= ((hsplit / 2) * img.Source.Width) - img.Source.Width / 2;
                    y_ += ((vsplit / 2) * img.Source.Height) - img.Source.Height / 2;

                    var trans = Transform.Create(
                        new Vector3(x_, y_, 0),
                        Vector3.Zero,
                        Vector3.Zero,
                        Vector3.Zero);

                    return trans;
                };
            }

            var hsplt = HSplit[args.Frame];
            var vsplt = VSplit[args.Frame];
            var sw = args.Value.Width / hsplt;
            var sh = args.Value.Height / vsplt;
            var result = new ImageInfo[(int)(hsplt * vsplt)];
            var count = 0;

            for (int v = 0; v < vsplt; v++)
            {
                for (int h = 0; h < hsplt; h++, count++)
                {
                    result[count] = new(
                        args.Value[new Rectangle((int)(sw * h), (int)(sh * v), (int)sw, (int)sh)],
                        GetTransform(h, v, hsplt, vsplt), count);
                }
            }

            return result;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            HSplit.Load(HSplitMetadata);
            VSplit.Load(VSplitMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            HSplit.Unload();
            VSplit.Unload();
        }
    }
}
