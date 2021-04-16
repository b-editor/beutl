using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Primitive.Objects;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Effects
{
    /// <summary>
    /// Represents an <see cref="ImageEffect"/> that adds a border to the image.
    /// </summary>
    public sealed class StrokeText : ImageEffect
    {
        /// <summary>
        /// Defines the <see cref="CenterX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<StrokeText, EaseProperty> CenterXProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, StrokeText>(
            nameof(CenterX), owner => owner.CenterX, (owner, obj) => owner.CenterX = obj, new EasePropertyMetadata(Strings.CenterX, 0));

        /// <summary>
        /// Defines the <see cref="CenterY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<StrokeText, EaseProperty> CenterYProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, StrokeText>(
            nameof(CenterY), owner => owner.CenterY, (owner, obj) => owner.CenterY = obj, new EasePropertyMetadata(Strings.CenterY, 0));

        /// <summary>
        /// Defines the <see cref="LineSpacing"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<StrokeText, EaseProperty> LineSpacingProperty = Text.LineSpacingProperty.WithOwner<StrokeText>(
            owner => owner.LineSpacing, (owner, obj) => owner.LineSpacing = obj, new EasePropertyMetadata(Strings.LineSpacing, 0));

        /// <summary>
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<StrokeText, EaseProperty> SizeProperty = Border.SizeProperty.WithOwner<StrokeText>(owner => owner.Size, (owner, obj) => owner.Size = obj);

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<StrokeText, ColorProperty> ColorProperty = Border.ColorProperty.WithOwner<StrokeText>(owner => owner.Color, (owner, obj) => owner.Color = obj);

        /// <summary>
        /// Initializes a new instance of the <see cref="StrokeText"/> class.
        /// </summary>
#pragma warning disable CS8618
        public StrokeText()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => $"{Strings.Border} ({Strings.Text})";

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            CenterX,
            CenterY,
            LineSpacing,
            Size,
            Color
        };

        /// <summary>
        /// Gets the X coordinate.
        /// </summary>
        public EaseProperty CenterX { get; private set; }

        /// <summary>
        /// Gets the Y coordinate.
        /// </summary>
        public EaseProperty CenterY { get; private set; }

        /// <summary>
        /// Gets the line spacing of the string to be drawn.
        /// </summary>
        public EaseProperty LineSpacing { get; private set; }

        /// <summary>
        /// Gets the size of the edge.
        /// </summary>
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the edge color.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<Image<BGRA32>> args)
        {
            if (Parent.Effect[0] is Text textObj)
            {
                var stroke = Image.StrokeText(
                    textObj.Document.Value,
                    textObj.Font.Value,
                    textObj.Size[args.Frame],
                    Size[args.Frame],
                    Color.Value,
                    (HorizontalAlign)textObj.HorizontalAlign.Index,
                    textObj.LineSpacing[args.Frame] + LineSpacing[args.Frame]);

                stroke.DrawImage(
                    new(((stroke.Width - args.Value.Width) / 2) + (int)CenterX[args.Frame], ((stroke.Height - args.Value.Height) / 2) + (int)CenterY[args.Frame]),
                    args.Value);

                args.Value.Dispose();

                args.Value = stroke;
            }
        }
        /// <inheritdoc/>
        public override void Render(EffectRenderArgs<IEnumerable<ImageInfo>> args)
        {
            if (Parent.Effect[0] is Text textObj)
            {
                if (!textObj.EnableMultiple.Value)
                {
                    var imageArgs = new EffectRenderArgs<Image<BGRA32>>(args.Frame, args.Value.First().Source, args.Type);
                    Render(imageArgs);
                    args.Value = new ImageInfo[]
                    {
                        new(imageArgs.Value, _ => default)
                    };

                    return;
                }
                args.Value = args.Value.Select((imageInfo, index) =>
                {
                    var stroke = Image.StrokeText(textObj.Document.Value, textObj.Font.Value, textObj.Size[args.Frame], Size[args.Frame], Color.Value, (HorizontalAlign)textObj.HorizontalAlign.Index);

                    stroke.DrawImage(
                        new((stroke.Width - imageInfo.Source.Width) / 2, (stroke.Height - imageInfo.Source.Height) / 2),
                        imageInfo.Source);

                    imageInfo.Dispose();

                    return new ImageInfo(stroke, img => Transform.Create(new(img.Source.Width * index, 0, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero));
                });
            }

        }
    }
}