// Text.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
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
        public static readonly DirectProperty<Text, EaseProperty> SizeProperty = EditingProperty.RegisterDirect<EaseProperty, Text>(
            nameof(Size),
            owner => owner.Size,
            (owner, obj) => owner.Size = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Size, 100, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="LineSpacing"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, EaseProperty> LineSpacingProperty = EditingProperty.RegisterDirect<EaseProperty, Text>(
            nameof(LineSpacing),
            owner => owner.LineSpacing,
            (owner, obj) => owner.LineSpacing = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.LineSpacing, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="CharacterSpacing"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, EaseProperty> CharacterSpacingProperty = EditingProperty.RegisterDirect<EaseProperty, Text>(
            nameof(CharacterSpacing),
            owner => owner.CharacterSpacing,
            (owner, obj) => owner.CharacterSpacing = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.CharacterSpacing, 0, float.NaN, 0)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, ColorProperty> ColorProperty = EditingProperty.RegisterDirect<ColorProperty, Text>(
            nameof(Color),
            owner => owner.Color,
            (owner, obj) => owner.Color = obj,
            EditingPropertyOptions<ColorProperty>.Create(new ColorPropertyMetadata(Strings.Color, Colors.White)).Serialize());

        /// <summary>
        /// Defines the <see cref="Color"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, FontProperty> FontProperty = EditingProperty.RegisterDirect<FontProperty, Text>(
            nameof(Font),
            owner => owner.Font,
            (owner, obj) => owner.Font = obj,
            EditingPropertyOptions<FontProperty>.Create(new FontPropertyMetadata()).Serialize());

        /// <summary>
        /// Defines the <see cref="TextAlignment"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, SelectorProperty> TextAlignmentProperty = EditingProperty.RegisterDirect<SelectorProperty, Text>(
            nameof(TextAlignment),
            owner => owner.TextAlignment,
            (owner, obj) => owner.TextAlignment = obj,
            EditingPropertyOptions<SelectorProperty>.Create(new SelectorPropertyMetadata(Strings.Alignment, new[]
            {
                Strings.Left,
                Strings.Center,
                Strings.Right,
            })).Serialize());

        /// <summary>
        /// Defines the <see cref="Document"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, DocumentProperty> DocumentProperty = EditingProperty.RegisterDirect<DocumentProperty, Text>(
            nameof(Document),
            owner => owner.Document,
            (owner, obj) => owner.Document = obj,
            EditingPropertyOptions<DocumentProperty>.Create(new DocumentPropertyMetadata(string.Empty)).Serialize());

        /// <summary>
        /// Defines the <see cref="IsMultiple"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, CheckProperty> IsMultipleProperty = EditingProperty.RegisterDirect<CheckProperty, Text>(
            nameof(IsMultiple),
            owner => owner.IsMultiple,
            (owner, obj) => owner.IsMultiple = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.EnableMultipleObjects, false)).Serialize());

        /// <summary>
        /// Defines the <see cref="AlignBaseline"/> property.
        /// </summary>
        public static readonly DirectProperty<Text, CheckProperty> AlignBaselineProperty = EditingProperty.RegisterDirect<CheckProperty, Text>(
            nameof(AlignBaseline),
            owner => owner.AlignBaseline,
            (owner, obj) => owner.AlignBaseline = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.AlignToBaseline, true)).Serialize());

        private FormattedText? _formattedText;

        /// <summary>
        /// Initializes a new instance of the <see cref="Text"/> class.
        /// </summary>
        public Text()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Text;

        /// <summary>
        /// Gets the size of the text.
        /// </summary>
        [AllowNull]
        public EaseProperty Size { get; private set; }

        /// <summary>
        /// Gets the line spacing of the text.
        /// </summary>
        [AllowNull]
        public EaseProperty LineSpacing { get; private set; }

        /// <summary>
        /// Gets the character spacing of the text.
        /// </summary>
        [AllowNull]
        public EaseProperty CharacterSpacing { get; private set; }

        /// <summary>
        /// Gets the color of text.
        /// </summary>
        [AllowNull]
        public ColorProperty Color { get; private set; }

        /// <summary>
        /// Gets the font of the text.
        /// </summary>
        [AllowNull]
        public FontProperty Font { get; private set; }

        /// <summary>
        /// Gets the horizontal alignment of the text.
        /// </summary>
        [AllowNull]
        public SelectorProperty TextAlignment { get; private set; }

        /// <summary>
        /// Gets the text.
        /// </summary>
        [AllowNull]
        public DocumentProperty Document { get; private set; }

        /// <summary>
        /// Gets the text.
        /// </summary>
        [AllowNull]
        public CheckProperty IsMultiple { get; private set; }

        /// <summary>
        /// Gets the align to the baseline.
        /// </summary>
        [AllowNull]
        public CheckProperty AlignBaseline { get; private set; }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return Coordinate;
            yield return Scale;
            yield return Blend;
            yield return Rotate;
            yield return Material;
            yield return Size;
            yield return LineSpacing;
            yield return CharacterSpacing;
            yield return Color;
            yield return Font;
            yield return TextAlignment;
            yield return Document;
            yield return IsMultiple;
            yield return AlignBaseline;
        }

        /// <inheritdoc/>
        protected override Image<BGRA32> OnRender(EffectApplyArgs args)
        {
            SetProperty(args.Frame);
            return _formattedText!.Draw();
        }

        /// <inheritdoc/>
        protected override void OnRender(EffectApplyArgs<IEnumerable<ImageInfo>> args)
        {
            if (IsMultiple.Value)
            {
                args.Value = Selector(args.Frame);
            }
            else
            {
                base.OnRender(args);
            }
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            base.OnLoad();
            _formattedText = new(string.Empty, Font.Value, 16, Drawing.TextAlignment.Left, new FormattedTextStyleSpan[] { new(0, 0..^1, Color.Value) });
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            base.OnUnload();
            _formattedText?.Dispose();
        }

        private void SetProperty(Frame frame)
        {
            _formattedText!.Text = Document.Value;
            _formattedText.Font = Font.Value;
            _formattedText.FontSize = Size[frame];
            _formattedText.LineSpacing = LineSpacing[frame];
            _formattedText.CharacterSpacing = CharacterSpacing[frame];
            _formattedText.TextAlignment = (TextAlignment)TextAlignment.Index;
            _formattedText.AlignBaseline = AlignBaseline.Value;
            _formattedText.Spans[0] = new(0, 0..^1, Color.Value);
        }

        private IEnumerable<ImageInfo> Selector(Frame frame)
        {
            SetProperty(frame);
            var bounds = _formattedText!.Bounds;

            return _formattedText.DrawMultiple().Select(c =>
            {
                return new ImageInfo(c.Image, _ =>
                {
                    var a = bounds;
                    var x = c.Rectangle.X + (c.Rectangle.Width / 2) - (bounds.Width / 2);
                    var y = c.Rectangle.Y + (c.Rectangle.Height / 2) - (bounds.Height / 2);
                    return new Transform(new(x, -y, 0), default, default, default);
                });
            });
        }
    }
}