using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Primitive;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that monochromatizes an image.
    /// </summary>
    [DataContract]
    public class Monoc : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light);

        /// <summary>
        /// Initializes a new instance of the <see cref="Monoc"/> class.
        /// </summary>
        public Monoc()
        {
            Color = new(ColorMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.Monoc;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Color
        };
        /// <summary>
        /// Get the <see cref="ColorProperty"/> that represents the color to be monochromatic.
        /// </summary>
        [DataMember(Order = 0)]
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.SetColor(Color.Color);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
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
