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
    /// Represents an <see cref="ImageEffect"/> that erodes an image.
    /// </summary>
    public sealed class Erode : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="Radius"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Erode, EaseProperty> RadiusProperty = Dilate.RadiusProperty.WithOwner<Erode>(
            owner => owner.Radius,
            (owner, obj) => owner.Radius = obj);

        /// <summary>
        /// Defines the <see cref="Resize"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Erode, CheckProperty> ResizeProperty = Dilate.ResizeProperty.WithOwner<Erode>(
            owner => owner.Resize,
            (owner, obj) => owner.Resize = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="Erode"/> class.
        /// </summary>
#pragma warning disable CS8618
        public Erode()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Erode;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Radius,
            Resize
        };

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the radius.
        /// </summary>
        public EaseProperty Radius { get; private set; }

        /// <summary>
        /// Gets a <see cref="CheckProperty"/> representing the value to resize the image.
        /// </summary>
        public CheckProperty Resize { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            var img = args.Value;
            var size = (int)Radius.GetValue(args.Frame);

            if (Resize.Value)
            {
                //Todo: 画像をリサイズ
                //int nwidth = img.Width - (size + 5) * 2;
                //int nheight = img.Height - (size + 5) * 2;

                //args.Value = img.MakeBorder(nwidth, nheight);
                args.Value.Erode(size);

                img.Dispose();
            }
            else
            {
                img.Erode(size);
            }
        }
    }
}