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
        /// Defines the <see cref="ThresholdValue"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ChromaKey, EaseProperty> TopProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, ChromaKey>(
            nameof(ThresholdValue),
            owner => owner.ThresholdValue,
            (owner, obj) => owner.ThresholdValue = obj,
            new EasePropertyMetadata(Strings.ThresholdValue, 256));

        /// <summary>
        /// Initializes a new instance of the <see cref="ChromaKey"/> class.
        /// </summary>
#pragma warning disable CS8618
        public ChromaKey()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ChromaKey;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return ThresholdValue;
            }
        }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the threshold.
        /// </summary>
        public EaseProperty ThresholdValue { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var context = Parent.Parent.DrawingContext;

            if (context is not null && Settings.Default.PrioritizeGPU)
            {
                args.Value.ChromaKey(context, (int)ThresholdValue[args.Frame]);
            }
            else
            {
                args.Value.ChromaKey((int)ThresholdValue[args.Frame]);
            }
        }
    }
}