using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ImageObject"/> to draw a string.
    /// </summary>
    public sealed class Text : ImageObject
    {
        /// <summary>
        /// Defines the <see cref="Size"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, EaseProperty> SizeProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Text>(
            nameof(Size), owner => owner.Size, (owner, obj) => owner.Size = obj, new EasePropertyMetadata(Strings.Size, 100, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="LineSpacing"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, EaseProperty> LineSpacingProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, Text>(
            nameof(LineSpacing), owner => owner.LineSpacing, (owner, obj) => owner.LineSpacing = obj, new EasePropertyMetadata(Strings.LineSpacing, 0, float.NaN, 0));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, ColorProperty> ColorProperty = EditingProperty.RegisterSerializeDirect<ColorProperty, Text>(
            nameof(Color), owner => owner.Color, (owner, obj) => owner.Color = obj, new ColorPropertyMetadata(Strings.Color, Drawing.Color.Light));

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, FontProperty> FontProperty = EditingProperty.RegisterSerializeDirect<FontProperty, Text>(
            nameof(Font), owner => owner.Font, (owner, obj) => owner.Font = obj, new FontPropertyMetadata());

        /// <summary>
        /// Defines the <see cref="HorizontalAlign"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, SelectorProperty> HorizontalAlignProperty = EditingProperty.RegisterSerializeDirect<SelectorProperty, Text>(
            nameof(HorizontalAlign),
            owner => owner.HorizontalAlign, (owner, obj) => owner.HorizontalAlign = obj,
            new SelectorPropertyMetadata(Strings.HorizontalAlignment, new[]
            {
                Strings.Left,
                Strings.Center,
                Strings.Right
            }));

        /// <summary>
        /// Defines the <see cref="VerticalAlign"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, SelectorProperty> VerticalAlignProperty = EditingProperty.RegisterSerializeDirect<SelectorProperty, Text>(
            nameof(VerticalAlign),
            owner => owner.VerticalAlign, (owner, obj) => owner.VerticalAlign = obj,
            new SelectorPropertyMetadata(Strings.VerticalAlignment, new[]
            {
                Strings.Top,
                Strings.Center,
                Strings.Bottom
            }));

        /// <summary>
        /// Defines the <see cref="Document"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, DocumentProperty> DocumentProperty = EditingProperty.RegisterSerializeDirect<DocumentProperty, Text>(
            nameof(Document), owner => owner.Document, (owner, obj) => owner.Document = obj, new DocumentPropertyMetadata(string.Empty));

        /// <summary>
        /// Defines the <see cref="EnableMultiple"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<Text, CheckProperty> EnableMultipleProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, Text>(
            nameof(EnableMultiple), owner => owner.EnableMultiple, (owner, obj) => owner.EnableMultiple = obj, new CheckPropertyMetadata(Strings.EnableMultipleObjects));

        /// <summary>
        /// Initializes a new instance of the <see cref="Text"/> class.
        /// </summary>
        public Text()
        {
        }

#nullable disable

        /// <inheritdoc/>
        public override string Name => Strings.Text;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Scale,
            Blend,
            Rotate,
            Material,
            Size,
            LineSpacing,
            Color,
            Font,
            HorizontalAlign,
            VerticalAlign,
            EnableMultiple,
            Document,
        };

        /// <summary>
        /// Gets the size of the string to be drawn.
        /// </summary>
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the line spacing of the string to be drawn.
        /// </summary>
        public EaseProperty LineSpacing { get; private set; }

        /// <summary>
        /// Gets the color of string to be drawn.
        /// </summary>
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Gets the font of the string to be drawn.
        /// </summary>
        public FontProperty Font { get; private set; }

        /// <summary>
        /// Gets the horizontal alignment of the string to be drawn.
        /// </summary>
        public SelectorProperty HorizontalAlign { get; private set; }

        /// <summary>
        /// Gets the vertical alignment of the string to be drawn.
        /// </summary>
        public SelectorProperty VerticalAlign { get; private set; }

        /// <summary>
        /// Gets whether or not to enable multiple objects.
        /// </summary>
        public CheckProperty EnableMultiple { get; private set; }

        /// <summary>
        /// Gets the string to be drawn.
        /// </summary>
        public DocumentProperty Document { get; private set; }

#nullable enable

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            return Image.Text(
                Document.Value,
                Font.Value,
                Size[args.Frame],
                Color.Value,
                (HorizontalAlign)HorizontalAlign.Index,
                (VerticalAlign)VerticalAlign.Index,
                LineSpacing[args.Frame]);
        }

        /// <inheritdoc/>
        protected override void OnRender(EffectRenderArgs<IEnumerable<ImageInfo>> args)
        {
            if (!EnableMultiple.Value)
            {
                args.Value = new ImageInfo[]
                {
                    new(OnRender(args as EffectRenderArgs), _ => default)
                };

                return;
            }
            args.Value = Document.Value
                .Select((c, index) => (Image.Text(c.ToString(), Font.Value, Size[args.Frame], Color.Value, (HorizontalAlign)HorizontalAlign.Index, (VerticalAlign)VerticalAlign.Index, LineSpacing[args.Frame]), index))
                .Select(t =>
                {
                    return new ImageInfo(t.Item1, img => GetTransform(img.Source.Width * t.index, 0));
                });
        }

        private static Transform GetTransform(int x, int y)
        {
            return Transform.Create(new(x, y, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero);
        }
    }
}