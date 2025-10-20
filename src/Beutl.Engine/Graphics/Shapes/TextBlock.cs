using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using SkiaSharp;

namespace Beutl.Graphics.Shapes;

[Display(Name = nameof(Strings.Text), ResourceType = typeof(Strings))]
public partial class TextBlock : Drawable
{
    public TextBlock()
    {
        ScanProperties<TextBlock>();
    }

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(Strings.FontWeight), ResourceType = typeof(Strings))]
    public IProperty<FontWeight> FontWeight { get; } = Property.Create(Media.FontWeight.Regular);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(Strings.FontStyle), ResourceType = typeof(Strings))]
    public IProperty<FontStyle> FontStyle { get; } = Property.Create(Media.FontStyle.Normal);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(Strings.FontFamily), ResourceType = typeof(Strings))]
    public IProperty<FontFamily?> FontFamily { get; } = Property.Create<FontFamily?>(Media.FontFamily.Default);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(Strings.Size), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public IProperty<float> Size { get; } = Property.Create<float>(12);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(Strings.CharactorSpacing), ResourceType = typeof(Strings))]
    public IProperty<float> Spacing { get; } = Property.Create<float>(0);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(Strings.Text), ResourceType = typeof(Strings))]
    [DataType(DataType.MultilineText)]
    public IProperty<string?> Text { get; } = Property.Create<string?>(string.Empty);

    [SuppressResourceClassGeneration]
    [Display(Name = nameof(Strings.Stroke), GroupName = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IProperty<Pen?> Pen { get; } = Property.Create<Pen?>();

    [Display(Name = nameof(Strings.SplitByCharacters), ResourceType = typeof(Strings))]
    public IProperty<bool> SplitByCharacters { get; } = Property.Create<bool>(false);

    protected override Size MeasureCore(Size availableSize, Drawable.Resource resource)
    {
        var r = (Resource)resource;
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

    internal static SKPath ToSKPath(TextElements elements)
    {
        var skpath = new SKPath();

        float prevBottom = 0;
        foreach (Span<FormattedText> line in elements.Lines)
        {
            Size lineBounds = MeasureLine(line);
            float ascent = MinAscent(line);
            var point = new Point(0, prevBottom - ascent);

            float prevRight = 0;
            foreach (FormattedText item in line)
            {
                if (item.Text.Length > 0)
                {
                    point += new Point(prevRight + item.Spacing / 2, 0);
                    Rect elementBounds = item.Bounds;

                    item.AddToSKPath(skpath, point);

                    prevRight = elementBounds.Width + item.Spacing;
                }
            }

            prevBottom += lineBounds.Height;
        }

        return skpath;
    }

    protected override void OnDraw(GraphicsContext2D context, Drawable.Resource resource)
    {
        var r= (Resource)resource;

        if (r.SplitByCharacters)
        {
            DrawSplitted(context, r);
        }
        else
        {
            DrawGrouped(context, r);
        }
    }

    private void DrawGrouped(GraphicsContext2D context, Resource resource)
    {
        var elements = resource.GetTextElements();
        using (context.Push())
        {
            float prevBottom = 0;
            foreach (Span<FormattedText> line in elements.Lines)
            {
                Size lineBounds = MeasureLine(line);
                float ascent = MinAscent(line);

                using (context.PushTransform(Matrix.CreateTranslation(0, prevBottom - ascent)))
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

    private void DrawSplitted(GraphicsContext2D context, Resource resource)
    {
        var elements = resource.GetTextElements();
        float prevBottom = 0;
        foreach (Span<FormattedText> line in elements.Lines)
        {
            Size lineBounds = MeasureLine(line);
            float ascent = MinAscent(line);
            float yPosition = prevBottom - ascent;

            float prevRight = 0;
            foreach (FormattedText item in line)
            {
                if (item.Text.Length > 0)
                {
                    Rect elementBounds = item.Bounds;

                    foreach (Geometry.Resource geometry in item.ToGeometies())
                    {
                        using (context.PushTransform(Matrix.CreateTranslation(prevRight + item.Spacing / 2, yPosition)))
                        {
                            context.DrawGeometry(geometry, item.Brush ?? resource.Fill, item.Pen ?? resource.Pen);
                        }
                    }

                    prevRight += elementBounds.Width + item.Spacing;
                }
            }

            prevBottom += lineBounds.Height;
        }
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

    private static float MinDescent(Span<FormattedText> items)
    {
        float descent = float.MaxValue;
        foreach (FormattedText item in items)
        {
            descent = MathF.Min(item.Metrics.Descent, descent);
        }

        return descent;
    }

    public partial class Resource
    {
        private Pen.Resource? _pen;
        private (Brush.Resource Resource, int Version)? _fillCache;
        private TextElements? _elements;
        private bool _isDirty = true;

        public FontStyle FontStyle { get; private set; }

        public FontFamily? FontFamily { get; private set; }

        public FontWeight FontWeight { get; private set; }

        public string? Text { get; private set; }

        public float Size { get; private set; }

        public float Spacing { get; private set; }

        public Pen.Resource? Pen => _pen;

        public TextElements GetTextElements()
        {
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

        partial void PreUpdate(TextBlock obj, RenderContext context)
        {
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
            CompareAndUpdateObject(context, obj.Pen, ref _pen, ref updated);
            if (updated)
            {
                _isDirty = true;
            }

            _fillCache = Fill.Capture();
        }

        partial void PostUpdate(TextBlock obj, RenderContext context)
        {
            var fill = Fill.Capture();
            if (fill != _fillCache)
            {
                _fillCache = fill;
                _isDirty = true;
            }

            if (_isDirty)
            {
                _elements = null;
                _isDirty = false;
            }
        }
    }
}
