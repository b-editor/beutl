using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents the <see cref="ImageEffect"/> that corrects the contrast of an image.
    /// </summary>
    public sealed class ContrastCorrection : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Contrast"/> property.
        /// </summary>
        public static readonly EditingProperty<EaseProperty> ContrastProperty = EditingProperty.RegisterDirect<EaseProperty, ContrastCorrection>(
            nameof(Contrast),
            owner => owner.Contrast,
            (owner, obj) => owner.Contrast = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Contrast, 0, 255, -255)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="ContrastCorrection"/> class.
        /// </summary>
        public ContrastCorrection()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.ContrastCorrection;

        /// <summary>
        /// Gets the contrast.
        /// </summary>
        [AllowNull]
        public EaseProperty Contrast { get; set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
        {
            args.Value.Contrast((short)Contrast[args.Frame]);
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Contrast;
        }
    }
}