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
    /// Represents a ColorKey effect.
    /// </summary>
    [DataContract]
    public class ColorKey : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="Color"/> metadata.
        /// </summary>
        public static readonly ColorPropertyMetadata ColorMetadata = new(Resources.Color, Drawing.Color.Light);
        /// <summary>
        /// Represents <see cref="ThresholdValue"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ThresholdValueMetadata = new(Resources.ThresholdValue, 60);

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorKey"/> class.
        /// </summary>
        public ColorKey()
        {
            Color = new(ColorMetadata);
            ThresholdValue = new(ThresholdValueMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Resources.ColorKey;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Color,
            ThresholdValue
        };
        /// <summary>
        /// Gets the <see cref="ColorProperty"/> representing the key color.
        /// </summary>
        [DataMember(Order = 0)]
        public ColorProperty Color { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the threshold.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty ThresholdValue { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.ColorKey(Color.Color, (int)ThresholdValue[args.Frame]);
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Color.Load(ColorMetadata);
            ThresholdValue.Load(ThresholdValueMetadata);
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
