using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using static BEditor.Primitive.Effects.Dilate;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that erodes an image.
    /// </summary>
    [DataContract]
    public class Erode : ImageEffect
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Erode"/> class.
        /// </summary>
        public Erode()
        {
            Radius = new(RadiusMetadata);
            Resize = new(ResizeMetadata);
        }

        /// <inheritdoc/>
        public override string Name => BEditor.Properties.Resources.Erode;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Radius,
            Resize
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the radius.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty Radius { get; private set; }
        /// <summary>
        /// Gets a <see cref="CheckProperty"/> representing the value to resize the image.
        /// </summary>
        [DataMember(Order = 1)]
        public CheckProperty Resize { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value;
            var size = (int)Radius.GetValue(args.Frame);

            if (Resize.IsChecked)
            {
                //Todo: 画像をリサイズ
                //int nwidth = img.Width - (size + 5) * 2;
                //int nheight = img.Height - (size + 5) * 2;

                //args.Value = img.MakeBorder(nwidth, nheight);
                args.Value.Erode(size);

                img.Dispose();
            }
            else
            {
                img.Erode(size);
            }
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Radius.Load(RadiusMetadata);
            Resize.Load(ResizeMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            foreach (var pr in Children)
            {
                pr.Unload();
            }
        }
    }
}
