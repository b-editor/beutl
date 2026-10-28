using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;

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

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.WritingMode == Media.WritingMode.VerticalRl)
            return r.GetVerticalLayout().Size;
        float width = 0;
        float height = 0;
        var elements = r.GetTextElements();

        foreach (Span<FormattedText> line in elements.Lines)
        {
            Size bounds = MeasureLine(line);
            width = MathF.Max(bounds.Width, width);
            height += bounds.Height;
        }

        return new Size(width, height);
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r = (Resource)resource;
        if (r.WritingMode == Media.WritingMode.VerticalRl)
        {
            DrawVertical(context, r);
            return;
        }
        float width = r.TextAlignment == Media.TextAlignment.Start ? 0 : MeasureCore(context.Size, resource).Width;

        if (r.SplitByCharacters)
        {
            DrawSplitted(context, r, width);
        }
        else
        {
            DrawGrouped(context, r, width);
        }
    }

    private static void DrawVertical(GraphicsContext2D context, Resource resource)
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
                using var transform = context.PushTransform(VerticalTextLayout.GetTransform(run, center, top));
                if (!resource.SplitByCharacters)
                {
                    context.DrawText(text, text.Brush ?? resource.Fill, text.Pen ?? resource.Pen);
                }
                else
                {
                    ReadOnlySpan<Geometry.Resource> geometries = text.ToGeometries();
                    for (int i = 0; i < geometries.Length; i++)
                    {
                        if (text.GetNonOutlineGlyph(i) is { } glyph)
                            context.DrawText(glyph, text.Brush ?? resource.Fill, text.Pen ?? resource.Pen);
                        else
                            context.DrawGeometry(geometries[i], text.Brush ?? resource.Fill, text.Pen ?? resource.Pen);
                    }
                }
            }
            right -= column.Width;
        }
    }

    private void DrawGrouped(GraphicsContext2D context, Resource resource, float width)
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

                            using (context.PushTransform(Matrix.CreateTranslation(prevRight + item.Spacing / 2, 0)))
                            {
                                context.DrawText(item, item.Brush ?? resource.Fill, item.Pen ?? resource.Pen);

                                prevRight += elementBounds.Width + item.Spacing;
                            }
                        }
                    }
                }

                prevBottom += lineBounds.Height;
            }
        }
    }

    private void DrawSplitted(GraphicsContext2D context, Resource resource, float width)
    {
        var elements = resource.GetTextElements();
        float prevBottom = 0;
        foreach (Span<FormattedText> line in elements.Lines)
        {
            Size lineBounds = MeasureLine(line);
            float ascent = MinAscent(line);
            float yPosition = prevBottom - ascent;

            float prevRight = GetLineOffset(resource.TextAlignment, width, lineBounds.Width);
            foreach (FormattedText item in line)
            {
                if (item.Text.Length > 0)
                {
                    Rect elementBounds = item.Bounds;

                    ReadOnlySpan<Geometry.Resource> geometries = item.ToGeometries();
                    for (int i = 0; i < geometries.Length; i++)
                    {
                        using (context.PushTransform(Matrix.CreateTranslation(prevRight + item.Spacing / 2, yPosition)))
                        {
                            if (item.GetNonOutlineGlyph(i) is { } glyph)
                            {
                                context.DrawText(glyph, item.Brush ?? resource.Fill, item.Pen ?? resource.Pen);
                            }
                            else
                            {
                                context.DrawGeometry(geometries[i], item.Brush ?? resource.Fill, item.Pen ?? resource.Pen);
                            }
                        }
                    }

                    prevRight += elementBounds.Width + item.Spacing;
                }
            }

            prevBottom += lineBounds.Height;
        }
    }

    private static float GetLineOffset(TextAlignment alignment, float width, float lineWidth)
    {
        return alignment switch
        {
            Media.TextAlignment.Center => (width - lineWidth) / 2,
            Media.TextAlignment.End => width - lineWidth,
            _ => 0,
        };
    }

    private static Size MeasureLine(Span<FormattedText> items)
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

    private static float MinAscent(Span<FormattedText> items)
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
        private bool _isDirty = true;
        private long _fontRevision;

        internal VerticalTextLayout GetVerticalLayout()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _verticalLayout ??= new VerticalTextLayout(GetTextElements());
        }

        public FontStyle FontStyle { get; private set; }

        public FontFamily? FontFamily { get; private set; }

        public FontWeight FontWeight { get; private set; }

        public string? Text { get; private set; }

        public float Size { get; private set; }

        public float Spacing { get; private set; }

        public Pen.Resource? Pen => _pen;

        public TextElements GetTextElements()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_elements == null)
            {
                if (string.IsNullOrEmpty(Text))
                {
                    _elements = new TextElements([]);
                }
                else
                {
                    var tokenizer = new FormattedTextTokenizer(Text);
                    tokenizer.Tokenize();
                    var options = new FormattedTextInfo(
                        Typeface: new Typeface(FontFamily ?? FontFamily.Default, FontStyle, FontWeight),
                        Size: Size,
                        Brush: Fill,
                        Space: Spacing,
                        Pen: Pen);

                    var builder = new TextElementsBuilder(options);
                    builder.AppendTokens(CollectionsMarshal.AsSpan(tokenizer.Result));
                    _elements = new TextElements(builder.Items.ToArray());
                }
            }

            return _elements;
        }

        partial void PostDispose(bool disposing)
        {
            _pen?.Dispose();
            _verticalLayout?.Dispose();
            _verticalLayout = null;
            _elements?.Dispose();
            _elements = null;
        }

        partial void PreReconcile(TextBlock obj, CompositionContext context)
        {
            long revision = FontManager.Instance.Revision;
            if (_fontRevision != revision)
            {
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
                _verticalLayout?.Dispose();
                _verticalLayout = null;
                _elements?.Dispose();
                _elements = null;
                _isDirty = false;
            }
        }
    }
}
