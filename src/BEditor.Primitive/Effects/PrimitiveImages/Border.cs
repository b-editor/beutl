using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Properties;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    public sealed class Border : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="Size"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata SizeMetadata = new(Resources.Size, 10, float.NaN, 1);
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light);

        /// <summary>
        /// Initializes a new instance of the <see cref="Border"/> class.
        /// </summary>
        public Border()
        {
            Size = new(SizeMetadata);
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Border;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Size,
            Color
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the size of the edge.
        /// </summary>
        [DataMember]
        public EaseProperty Size { get; private set; }
        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the edge color.
        /// </summary>
        [DataMember]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value.Border((int)Size.GetValue(args.Frame), Color.Value);
            args.Value.Dispose();

            args.Value = img;
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Size.Load(SizeMetadata);
            Color.Load(ColorMetadata);
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
