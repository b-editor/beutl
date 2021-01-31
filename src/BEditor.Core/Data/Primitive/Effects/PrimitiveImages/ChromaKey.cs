using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that makes the background color of an image transparent.
    /// </summary>
    [DataContract]
    public class ChromaKey : ImageEffect
    {
        /// <summary>
        /// Represents <see cref="ThresholdValue"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ThresholdValueMetadata = new("閾値", 256);

        /// <summary>
        /// Initializes a new instance of the <see cref="ChromaKey"/> class.
        /// </summary>
        public ChromaKey()
        {
            ThresholdValue = new(ThresholdValueMetadata);
        }

        /// <inheritdoc/>
        public override string Name => "Chrome key";
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            ThresholdValue
        };
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the threshold.
        /// </summary>
        [DataMember(Order = 0)]
        public EaseProperty ThresholdValue { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            args.Value.ChromeKey((int)(ThresholdValue[args.Frame]));
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
