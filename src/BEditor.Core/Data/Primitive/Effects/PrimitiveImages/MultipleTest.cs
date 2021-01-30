using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

namespace BEditor.Core.Data.Primitive.Effects
{
    [DataContract]
    public class MultipleTest : MultipleImageEffect
    {
        public MultipleTest()
        {
        }

        public override string Name => "MultipleTest";
        public override IEnumerable<PropertyElement> Properties => Array.Empty<PropertyElement>();

        public override IEnumerable<ImageInfo> MultipleRender(EffectRenderArgs<Image<BGRA32>> args)
        {
            var w = args.Value.Width;
            var h = args.Value.Height;

            var img1 = args.Value[new Rectangle(0, 0, w / 2, h / 2)];
            var img2 = args.Value[new Rectangle(0, h / 2, w / 2, h / 2)];
            var img3 = args.Value[new Rectangle(w / 2, 0, w / 2, h / 2)];
            var img4 = args.Value[new Rectangle(w / 2, h / 2, w / 2, h / 2)];

            return new ImageInfo[]
            {
                new ImageInfo(img1, img => Transform.Create(
                    new Vector3(-100, 100, 0),
                    Vector3.Zero,
                    Vector3.Zero,
                    Vector3.Zero)),
                new ImageInfo(img2, img => Transform.Create(
                    new Vector3(100, -100, 0),
                    Vector3.Zero,
                    Vector3.Zero,
                    Vector3.Zero)),
                new ImageInfo(img3, img => Transform.Create(
                    new Vector3(100, 100, 0),
                    Vector3.Zero,
                    Vector3.Zero,
                    Vector3.Zero)),
                new ImageInfo(img4, img => Transform.Create(
                    new Vector3( -100, -100, 0),
                    Vector3.Zero,
                    Vector3.Zero,
                    Vector3.Zero))
            };
        }
    }
}
