using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that makes the background color of an image transparent.
    /// </summary>
    public sealed class ChromaKey : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="ThresholdValue"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ThresholdValueMetadata = new(Strings.ThresholdValue, 256);

        /// <summary>
        /// Initializes a new instance of the <see cref="ChromaKey"/> class.
        /// </summary>
        public ChromaKey()
        {
            ThresholdValue = new(ThresholdValueMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.ChromaKey;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            ThresholdValue
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the threshold.
        /// </summary>
        [DataMember]
        public EaseProperty ThresholdValue { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.ChromaKey((int)(ThresholdValue[args.Frame]));
        }
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            ThresholdValue.Load(ThresholdValueMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            ThresholdValue.Unload();
        }
    }
}
