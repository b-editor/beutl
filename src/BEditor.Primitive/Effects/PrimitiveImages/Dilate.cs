using System.Collections.Generic;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that dilates an image.
    /// </summary>
    public sealed class Dilate : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="Radius"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata RadiusMetadata = new(Resources.Frequency, 1, float.NaN, 0);
        /// <summary>
        /// Represents <see cref="Resize"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata ResizeMetadata = new(Resources.Resize);

        /// <summary>
        /// Initializes a new instance of the <see cref="Dilate"/> class.
        /// </summary>
        public Dilate()
        {
            Radius = new(RadiusMetadata);
            Resize = new(ResizeMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Dilate;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Radius,
            Resize
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the radius.
        /// </summary>
        [DataMember]
        public EaseProperty Radius { get; private set; }
        /// <summary>
        /// Gets a <see cref="CheckProperty"/> representing the value to resize the image.
        /// </summary>
        [DataMember]
        public CheckProperty Resize { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value;
            var size = (int)Radius.GetValue(args.Frame);
            if (Resize.Value)
            {
                int nwidth = img.Width + (size + 5) * 2;
                int nheight = img.Height + (size + 5) * 2;

                args.Value = img.MakeBorder(nwidth, nheight);
                args.Value.Dilate(size);

                img.Dispose();
            }
            else
            {
                img.Dilate(size);
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
