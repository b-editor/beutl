using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(GraphicsStrings.TextBlock), ResourceType = typeof(GraphicsStrings))]
public partial class TextBlock : Drawable
{
    public TextBlock()
    {
        ScanProperties<TextBlock>();
        Fill.Value = new SolidColorBrush(Colors.White);
    }

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.TextBlock_Size), ResourceType = typeof(GraphicsStrings))]
    [Range(0, float.MaxValue)]
    public IExpressionProperty<float> Size { get; } = Property.CreateAnimatable<float>(72);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.TextBlock_FontFamily), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<FontFamily?> FontFamily { get; } = Property.CreateExpression<FontFamily?>(Media.FontFamily.Default);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.TextBlock_FontStyle), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<FontStyle> FontStyle { get; } = Property.CreateExpression(Media.FontStyle.Normal);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.TextBlock_FontWeight), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<FontWeight> FontWeight { get; } = Property.CreateExpression(Media.FontWeight.Regular);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.TextBlock_Spacing), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<float> Spacing { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.TextBlock_TextAlignment), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<TextAlignment> TextAlignment { get; } = Property.CreateAnimatable(Media.TextAlignment.Start);

    [Display(Name = nameof(GraphicsStrings.TextBlock_WritingMode), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<WritingMode> WritingMode { get; } = Property.CreateExpression(Media.WritingMode.HorizontalTb);

    [Display(Name = nameof(GraphicsStrings.TextBlock_SplitByCharacters), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<bool> SplitByCharacters { get; } = Property.CreateAnimatable<bool>(false);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.TextBlock_Text), ResourceType = typeof(GraphicsStrings))]
    [DataType(DataType.MultilineText)]
    public IExpressionProperty<string?> Text { get; } = Property.CreateExpression<string?>(string.Empty);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(GraphicsStrings.Stroke), GroupName = nameof(GraphicsStrings.Stroke), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Pen?> Pen { get; } = Property.CreateExpression<Pen?>();

    [Display(Name = nameof(GraphicsStrings.Fill), ResourceType = typeof(GraphicsStrings), GroupName = nameof(GraphicsStrings.Fill))]
    public IExpressionProperty<Brush?> Fill { get; } = Property.CreateExpression<Brush?>();

    private static void DrawVertical(GraphicsContext2D context, Resource resource, bool strokesBelowFill)
    {
        VerticalTextLayout layout = resource.GetVerticalLayout();
        float right = layout.Size.Width;
        foreach (var column in layout.Columns)
        {
            float center = right - column.Width / 2;
            float top = GetLineOffset(resource.TextAlignment, layout.Size.Height, column.Height);
            foreach (var run in column.Runs)
            {
                FormattedText text = run.Text;
                if (!TrySelectPaint(strokesBelowFill, text.Brush ?? resource.Fill, text.Pen ?? resource.Pen,
                        out Brush.Resource? fill, out Pen.Resource? pen))
                {
                    continue;
                }

                using var transform = context.PushTransform(VerticalTextLayout.GetTransform(run, center, top));
                context.DrawText(text, fill, pen);
            }
            right -= column.Width;
        }
    }

    private static void DrawGrouped(GraphicsContext2D context, Resource resource, float width, bool strokesBelowFill)
    {
        var elements = resource.GetTextElements();
        using (context.Push())
        {
            float prevBottom = 0;
            foreach (Span<FormattedText> line in elements.Lines)
            {
                Size lineBounds = MeasureLine(line);
                float ascent = MinAscent(line);
                float x = GetLineOffset(resource.TextAlignment, width, lineBounds.Width);

                using (context.PushTransform(Matrix.CreateTranslation(x, prevBottom - ascent)))
                {
                    float prevRight = 0;
                    foreach (FormattedText item in line)
                    {
                        if (item.Text.Length > 0)
                        {
                            Rect elementBounds = item.Bounds;

                            if (TrySelectPaint(strokesBelowFill, item.Brush ?? resource.Fill, item.Pen ?? resource.Pen,
                                    out Brush.Resource? fill, out Pen.Resource? pen))
                            {
                                using (context.PushTransform(Matrix.CreateTranslation(prevRight + item.Spacing / 2, 0)))
                                {
                                    context.DrawText(item, fill, pen);
                                }
                            }

                            prevRight += elementBounds.Width + item.Spacing;
                        }
                    }
                }

                prevBottom += lineBounds.Height;
            }
        }
    }

    private static void DrawSplit(GraphicsContext2D context, Resource resource, bool strokesBelowFill,
        TextPartLayout parts, long occurrence)
    {
        foreach (TextPartLayout.Run run in parts.Runs)
        {
            FormattedText text = run.Text;
            if (!TrySelectPaint(strokesBelowFill, text.Brush ?? resource.Fill, text.Pen ?? resource.Pen,
                    out Brush.Resource? fill, out Pen.Resource? pen))
            {
                continue;
            }

            using var transform = context.PushTransform(run.Transform);
            ReadOnlySpan<Geometry.Resource> geometries = text.ToGeometries();
            for (int i = 0; i < geometries.Length; i++)
            {
                DrawSplitGlyph(context, text, geometries[i], i, fill, pen, parts.CreateTag(occurrence, run.Glyphs[i]));
            }
        }
    }

    private static void DrawSplitGlyph(GraphicsContext2D context, FormattedText text, Geometry.Resource geometry,
        int index, Brush.Resource? fill, Pen.Resource? pen, TextPartTag tag)
    {
        if (text.GetNonOutlineGlyph(index) is { } glyph)
            context.DrawText(glyph, fill, pen, tag);
        else
            context.DrawGeometry(geometry, fill, pen, tag);
    }

    private static bool IsStrokeBelowFill(Pen.Resource? pen)
    {
        return pen != null && PenHelper.DrawsBeneathFill(pen);
    }

    // The pass that draws the strokes placed below the fill paints only those strokes; the other pass paints every
    // fill and the strokes placed above it. Returns false when the run has nothing to paint in this pass.
    private static bool TrySelectPaint(
        bool strokesBelowFill,
        Brush.Resource? fill,
        Pen.Resource? pen,
        out Brush.Resource? passFill,
        out Pen.Resource? passPen)
    {
        bool below = IsStrokeBelowFill(pen);
        passFill = strokesBelowFill ? null : fill;
        passPen = strokesBelowFill == below ? pen : null;
        return !strokesBelowFill || below;
    }

    internal static float GetLineOffset(TextAlignment alignment, float width, float lineWidth)
    {
        return alignment switch
        {
            Media.TextAlignment.Center => (width - lineWidth) / 2,
            Media.TextAlignment.End => width - lineWidth,
            _ => 0,
        };
    }

    internal static Size MeasureLine(Span<FormattedText> items)
    {
        float width = 0;
        float height = 0;

        foreach (FormattedText item in items)
        {
            if (item.Text.Length > 0)
            {
                Rect bounds = item.Bounds;
                width += bounds.Width;
                width += item.Spacing;

                height = MathF.Max(item.Metrics.Leading + item.Metrics.Descent - item.Metrics.Ascent, height);
            }
        }

        return new Size(width, height);
    }

    internal static float MinAscent(Span<FormattedText> items)
    {
        float ascent = 0;
        foreach (FormattedText item in items)
        {
            ascent = MathF.Min(item.Metrics.Ascent, ascent);
        }

        return ascent;
    }

    public partial class Resource
    {
        private Pen.Resource? _pen;
        private (Brush.Resource Resource, int Version)? _fillCache;
        private TextElements? _elements;
        private VerticalTextLayout? _verticalLayout;
        private TextPartLayout? _textPartLayout;
        private int _textPartLayoutRevision;
        private long _textPartFontRevision;
        private Dictionary<(SKTypeface Typeface, string Cluster), bool>? _verticalAlternates;
        private bool _isDirty = true;
        private long _fontRevision;
        private FontManager? _fonts;

        internal VerticalTextLayout GetVerticalLayout()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _verticalLayout ??= new VerticalTextLayout(GetTextElements(), _verticalAlternates ??= []);
        }

        internal TextPartLayout GetTextPartLayout()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            long fontRevision = _fonts?.Revision ?? 0;
            // FormattedText can reshape on a font revision before the drawable reconciles again.
            if (_textPartLayout is { } layout && layout.WritingMode == WritingMode
                && layout.Positions.Alignment == TextAlignment && _textPartFontRevision == fontRevision)
                return layout;

            VerticalTextLayout? vertical = WritingMode == Media.WritingMode.VerticalRl ? GetVerticalLayout() : null;
            float width = vertical is null && TextAlignment != Media.TextAlignment.Start
                ? MeasureCore(Graphics.Size.Infinity).Width
                : 0;
            layout = TextPartLayout.Create(
                GetTextElements(), vertical, WritingMode, TextAlignment, width, ++_textPartLayoutRevision);
            _textPartFontRevision = fontRevision;
            return _textPartLayout = layout;
        }

        public FontStyle FontStyle { get; private set; }

        public FontFamily? FontFamily { get; private set; }

        public FontWeight FontWeight { get; private set; }

        public string? Text { get; private set; }

        public float Size { get; private set; }

        public float Spacing { get; private set; }

        public Pen.Resource? Pen => _pen;

        protected override Size MeasureCore(Size availableSize)
        {
            if (WritingMode == Media.WritingMode.VerticalRl)
                return GetVerticalLayout().Size;
            float width = 0;
            float height = 0;
            var elements = GetTextElements();

            foreach (Span<FormattedText> line in elements.Lines)
            {
                Size bounds = MeasureLine(line);
                width = MathF.Max(bounds.Width, width);
                height += bounds.Height;
            }

            return new Size(width, height);
        }

        protected override void OnDraw(GraphicsContext2D context)
        {
            TextPartLayout? parts = SplitByCharacters ? GetTextPartLayout() : null;
            long occurrence = parts is null ? 0 : context.GetTextPartOccurrence();
            // A stroke placed below the fill goes below every fill in the block, not only its own, so all those
            // strokes are drawn in a pass of their own before any fill.
            if (HasStrokeBelowFill())
                DrawText(context, strokesBelowFill: true, parts, occurrence);

            DrawText(context, strokesBelowFill: false, parts, occurrence);
        }

        private void DrawText(GraphicsContext2D context, bool strokesBelowFill, TextPartLayout? parts, long occurrence)
        {
            if (parts is not null)
            {
                DrawSplit(context, this, strokesBelowFill, parts, occurrence);
            }
            else if (WritingMode == Media.WritingMode.VerticalRl)
            {
                DrawVertical(context, this, strokesBelowFill);
            }
            else
            {
                float width = TextAlignment == Media.TextAlignment.Start ? 0 : MeasureCore(context.Size).Width;
                DrawGrouped(context, this, width, strokesBelowFill);
            }
        }

        private bool HasStrokeBelowFill()
        {
            foreach (TextElement element in GetTextElements())
            {
                if (IsStrokeBelowFill(element.Pen ?? Pen))
                    return true;
            }

            return false;
        }

        public TextElements GetTextElements()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_elements == null)
            {
                FontManager fonts = _fonts ?? throw new InvalidOperationException("Text layout requires CompositionContext.Fonts.");
                if (string.IsNullOrEmpty(Text))
                {
                    _elements = new TextElements(fonts, []);
                }
                else
                {
                    var tokenizer = new FormattedTextTokenizer(Text);
                    tokenizer.Tokenize();
                    var options = new FormattedTextInfo(
                        Typeface: new Typeface(FontFamily ?? fonts.DefaultTypeface.FontFamily, FontStyle, FontWeight),
                        Size: Size,
                        Brush: Fill,
                        Space: Spacing,
                        Pen: Pen);

                    var builder = new TextElementsBuilder(fonts, options);
                    builder.AppendTokens(CollectionsMarshal.AsSpan(tokenizer.Result));
                    _elements = new TextElements(fonts, builder.Items.ToArray());
                }
            }

            return _elements;
        }

        partial void PostDispose(bool disposing)
        {
            _textPartLayout = null;
            _pen?.Dispose();
            _verticalLayout?.Dispose();
            _verticalLayout = null;
            _verticalAlternates = null;
            _elements?.Dispose();
            _elements = null;
        }

        partial void PreReconcile(TextBlock obj, CompositionContext context)
        {
            FontManager? fonts = context.Fonts;
            long revision = fonts?.Revision ?? 0;
            if (!ReferenceEquals(_fonts, fonts) || _fontRevision != revision)
            {
                _fonts = fonts;
                _fontRevision = revision;
                _isDirty = true;
            }
            var fontStyle = context.Get(obj.FontStyle);
            if (FontStyle != fontStyle)
            {
                FontStyle = fontStyle;
                _isDirty = true;
            }

            var fontFamily = context.Get(obj.FontFamily);
            if (!Equals(FontFamily, fontFamily))
            {
                FontFamily = fontFamily;
                _isDirty = true;
            }

            var fontWeight = context.Get(obj.FontWeight);
            if (FontWeight != fontWeight)
            {
                FontWeight = fontWeight;
                _isDirty = true;
            }

            // Alternates are keyed by typeface and cluster, so only font selection invalidates them; text, size,
            // spacing and paint do not.
            if (_isDirty)
                _verticalAlternates?.Clear();

            var text = context.Get(obj.Text);
            if (Text != text)
            {
                Text = text;
                _isDirty = true;
            }

            var size = context.Get(obj.Size);
            if (Size != size)
            {
                Size = size;
                _isDirty = true;
            }

            var spacing = context.Get(obj.Spacing);
            if (Spacing != spacing)
            {
                Spacing = spacing;
                _isDirty = true;
            }

            var updated = false;
            ResourceReconciler.ReconcileChild(this, context, obj.Pen, ref _pen, ref updated);
            if (updated)
            {
                _isDirty = true;
            }

            _fillCache = Fill.Capture();
        }

        partial void PostReconcile(TextBlock obj, CompositionContext context)
        {
            var fill = Fill.Capture();
            if (fill != _fillCache)
            {
                _isDirty = true;
            }

            if (_isDirty)
            {
                Version++;
                _textPartLayout = null;
                _verticalLayout?.Dispose();
                _verticalLayout = null;
                _elements?.Dispose();
                _elements = null;
                _isDirty = false;
            }
        }
    }
}
