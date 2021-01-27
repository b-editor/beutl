using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects
{
    /// <summary>
    /// Represents a ColorKey effect.
    /// </summary>
    [DataContract]
    public class ColorKey : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="MaxColor"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata MaxColorMetadata = new(Resources.Color, Color.Light);
        /// <summary>
        /// Represents <see cref="MinColor"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata MinColorMetadata = new(Resources.Color, Color.FromARGB(100, 100, 100, 255));

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorKey"/> class.
        /// </summary>
        public ColorKey()
        {
            MaxColor = new(MaxColorMetadata);
            MinColor = new(MinColorMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.ColorKey;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            MaxColor,
            MinColor
        };
        /// <summary>
        /// Gets the <see cref="ColorProperty"/> representing the maximum value.
        /// </summary>
        [DataMember(Order = 0)]
        public ColorProperty MaxColor { get; private set; }
        /// <summary>
        /// Gets the <see cref="ColorProperty"/> representing the minimum value.
        /// </summary>
        [DataMember(Order = 1)]
        public ColorProperty MinColor { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args) { }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            MaxColor.Load(MaxColorMetadata);
            MinColor.Load(MinColorMetadata);
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
