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
    /// Represents the <see cref="ImageEffect"/> that dilates an image.
    /// </summary>
    public sealed class Dilate : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Dilate, EaseProperty> RadiusProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Dilate>(
            nameof(Radius),
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj,
            new EasePropertyMetadata(Strings.Radius, 1, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="Resize"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Dilate, CheckProperty> ResizeProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, Dilate>(
            nameof(Resize),
            owner => owner.Resize,
            (owner, obj) => owner.Resize = obj,
            new CheckPropertyMetadata(Strings.Resize));

        /// <summary>
        /// Initializes a new instance of the <see cref="Dilate"/> class.
        /// </summary>
        public Dilate()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Dilate;

        /// <summary>
        /// Gets the radius.
        /// </summary>
        [AllowNull]
        public EaseProperty Radius { get; private set; }

        /// <summary>
        /// Gets the value to resize the image.
        /// </summary>
        [AllowNull]
        public CheckProperty Resize { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs<Image<BGRA32>> args)
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
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Radius;
            yield return Resize;
        }
    }
}