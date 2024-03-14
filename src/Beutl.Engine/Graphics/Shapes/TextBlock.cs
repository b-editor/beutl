using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;
using Beutl.Serialization;

using SkiaSharp;

namespace Beutl.Graphics.Shapes;

public class TextBlock : Drawable
{
    public static readonly CoreProperty<FontFamily?> FontFamilyProperty;
    public static readonly CoreProperty<FontWeight> FontWeightProperty;
    public static readonly CoreProperty<FontStyle> FontStyleProperty;
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<float> SpacingProperty;
    public static readonly CoreProperty<string?> TextProperty;
    public static readonly CoreProperty<IPen?> PenProperty;
    public static readonly CoreProperty<bool> SplitByCharactersProperty;
    public static readonly CoreProperty<TextElements?> ElementsProperty;
    private FontFamily? _fontFamily = FontFamily.Default;
    private FontWeight _fontWeight = FontWeight.Regular;
    private FontStyle _fontStyle = FontStyle.Normal;
    private float _size;
    private float _spacing;
    private string? _text = string.Empty;
    private IPen? _pen = null;
    private bool _splitByCharacters = false;
    private TextElements? _elements;
    private bool _isDirty;

    static TextBlock()
    {
        FontWeightProperty = ConfigureProperty<FontWeight, TextBlock>(nameof(FontWeight))
            .Accessor(o => o.FontWeight, (o, v) => o.FontWeight = v)
            .DefaultValue(FontWeight.Regular)
            .Register();

        FontStyleProperty = ConfigureProperty<FontStyle, TextBlock>(nameof(FontStyle))
            .Accessor(o => o.FontStyle, (o, v) => o.FontStyle = v)
            .DefaultValue(FontStyle.Normal)
            .Register();

        FontFamilyProperty = ConfigureProperty<FontFamily?, TextBlock>(nameof(FontFamily))
            .Accessor(o => o.FontFamily, (o, v) => o.FontFamily = v)
            .DefaultValue(FontFamily.Default)
            .Register();

        SizeProperty = ConfigureProperty<float, TextBlock>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .DefaultValue(0)
            .Register();

        SpacingProperty = ConfigureProperty<float, TextBlock>(nameof(Spacing))
            .Accessor(o => o.Spacing, (o, v) => o.Spacing = v)
            .DefaultValue(0)
            .Register();

        TextProperty = ConfigureProperty<string?, TextBlock>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .DefaultValue(string.Empty)
            .Register();

        PenProperty = ConfigureProperty<IPen?, TextBlock>(nameof(Pen))
            .Accessor(o => o.Pen, (o, v) => o.Pen = v)
            .Register();

        SplitByCharactersProperty = ConfigureProperty<bool, TextBlock>(nameof(SplitByCharacters))
            .Accessor(o => o.SplitByCharacters, (o, v) => o.SplitByCharacters = v)
            .Register();

        ElementsProperty = ConfigureProperty<TextElements?, TextBlock>(nameof(Elements))
            .Accessor(o => o.Elements, (o, v) => o.Elements = v)
            .Register();

        AffectsRender<TextBlock>(
            FontWeightProperty,
            FontStyleProperty,
            FontFamilyProperty,
            SizeProperty,
            SpacingProperty,
            TextProperty,
            PenProperty,
            SplitByCharactersProperty,
            ElementsProperty);
    }

    public TextBlock()
    {
        Invalidated += OnInvalidated;
    }

    [Display(Name = nameof(Strings.FontWeight), ResourceType = typeof(Strings))]
    public FontWeight FontWeight
    {
        get => _fontWeight;
        set => SetAndRaise(FontWeightProperty, ref _fontWeight, value);
    }

    [Display(Name = nameof(Strings.FontStyle), ResourceType = typeof(Strings))]
    public FontStyle FontStyle
    {
        get => _fontStyle;
        set => SetAndRaise(FontStyleProperty, ref _fontStyle, value);
    }

    [Display(Name = nameof(Strings.FontFamily), ResourceType = typeof(Strings))]
    public FontFamily? FontFamily
    {
        get => _fontFamily;
        set => SetAndRaise(FontFamilyProperty, ref _fontFamily, value);
    }

    [Display(Name = nameof(Strings.Size), ResourceType = typeof(Strings))]
    [Range(0, float.MaxValue)]
    public float Size
    {
        get => _size;
        set => SetAndRaise(SizeProperty, ref _size, value);
    }

    [Display(Name = nameof(Strings.CharactorSpacing), ResourceType = typeof(Strings))]
    public float Spacing
    {
        get => _spacing;
        set => SetAndRaise(SpacingProperty, ref _spacing, value);
    }

    [Display(Name = nameof(Strings.Text), ResourceType = typeof(Strings))]
    [DataType(DataType.MultilineText)]
    public string? Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    [Display(Name = nameof(Strings.Stroke), GroupName = nameof(Strings.Stroke), ResourceType = typeof(Strings))]
    public IPen? Pen
    {
        get => _pen;
        set => SetAndRaise(PenProperty, ref _pen, value);
    }

    [Display(Name = nameof(Strings.SplitByCharacters), ResourceType = typeof(Strings))]
    public bool SplitByCharacters
    {
        get => _splitByCharacters;
        set => SetAndRaise(SplitByCharactersProperty, ref _splitByCharacters, value);
    }

    [NotAutoSerialized]
    public TextElements? Elements
    {
        get => _elements;
        set => SetAndRaise(ElementsProperty, ref _elements, value);
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        OnUpdateText();
        context.SetValue(nameof(Elements), Elements?.ToArray());
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);
        if (context.GetValue<TextElement[]>(nameof(Elements)) is { } elements)
        {
            Elements = new TextElements(elements);
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        OnUpdateText();
        float width = 0;
        float height = 0;

        if (_elements != null)
        {
            float lastDescent = 0f;
            foreach (Span<FormattedText> line in _elements.Lines)
            {
                Size bounds = MeasureLine(line);
                width = MathF.Max(bounds.Width, width);
                height += bounds.Height;

                lastDescent = MinDescent(line);
            }

            height -= lastDescent;
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

    protected override void OnDraw(ICanvas canvas)
    {
        OnUpdateText();
        if (_elements != null)
        {
            if (SplitByCharacters)
            {
                DrawSplitted(canvas, _elements);
            }
            else
            {
                DrawGrouped(canvas, _elements);
            }
        }
    }

    private void DrawGrouped(ICanvas canvas, TextElements elements)
    {
        using (canvas.Push())
        {
            float prevBottom = 0;
            foreach (Span<FormattedText> line in elements.Lines)
            {
                Size lineBounds = MeasureLine(line);
                float ascent = MinAscent(line);
                float descent = MinDescent(line);

                using (canvas.PushTransform(Matrix.CreateTranslation(0, prevBottom - ascent - descent)))
                {
                    float prevRight = 0;
                    foreach (FormattedText item in line)
                    {
                        if (item.Text.Length > 0)
                        {
                            Rect elementBounds = item.Bounds;

                            using (canvas.PushTransform(Matrix.CreateTranslation(prevRight + item.Spacing / 2, 0)))
                            {
                                canvas.DrawText(item, item.Brush ?? Fill, item.Pen ?? Pen);

                                prevRight += elementBounds.Width + item.Spacing;
                            }
                        }
                    }
                }

                prevBottom += lineBounds.Height;
            }
        }
    }

    private void DrawSplitted(ICanvas canvas, TextElements elements)
    {
        float prevBottom = 0;
        foreach (Span<FormattedText> line in elements.Lines)
        {
            Size lineBounds = MeasureLine(line);
            float ascent = MinAscent(line);
            float descent = MinDescent(line);
            float yPosition = prevBottom - ascent - descent;

            float prevRight = 0;
            foreach (FormattedText item in line)
            {
                if (item.Text.Length > 0)
                {
                    Rect elementBounds = item.Bounds;

                    foreach (Geometry geometry in item.ToGeometies())
                    {
                        using (canvas.PushTransform(Matrix.CreateTranslation(prevRight + item.Spacing / 2, yPosition)))
                        {
                            canvas.DrawGeometry(geometry, item.Brush ?? Fill, item.Pen ?? Pen);
                        }
                    }

                    prevRight += elementBounds.Width + item.Spacing;
                }
            }

            prevBottom += lineBounds.Height;
            yPosition = prevBottom - ascent - descent;
        }
    }

    private void OnInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        if (ReferenceEquals(e.Sender, this)
            && e.PropertyName is nameof(FontStyle)
                or nameof(FontFamily)
                or nameof(FontWeight)
                or nameof(Text)
                or nameof(Size)
                or nameof(Fill)
                or nameof(Spacing)
                or nameof(Pen))
        {
            _isDirty = true;
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args is CorePropertyChangedEventArgs e)
        {
            if (e.Property == FillProperty || e.Property == PenProperty)
            {
                void OnBrushOrPenInvalidated(object? sender, RenderInvalidatedEventArgs e)
                {
                    _isDirty = true;
                }

                if (e.OldValue is IAffectsRender oldValue)
                {
                    oldValue.Invalidated -= OnBrushOrPenInvalidated;
                }

                if (e.NewValue is IAffectsRender newValue)
                {
                    newValue.Invalidated += OnBrushOrPenInvalidated;
                }
            }
        }
    }

    private void OnUpdateText()
    {
        if (_isDirty)
        {
            if (string.IsNullOrEmpty(_text))
            {
                Elements = new TextElements([]);
            }
            else
            {
                var tokenizer = new FormattedTextTokenizer(_text);
                tokenizer.Tokenize();
                var options = new FormattedTextInfo(
                    Typeface: new Typeface(_fontFamily ?? FontFamily.Default, _fontStyle, _fontWeight),
                    Size: _size,
                    Brush: (Fill as IMutableBrush)?.ToImmutable(),
                    Space: _spacing,
                    Pen: _pen);

                var builder = new TextElementsBuilder(options);
                builder.AppendTokens(CollectionsMarshal.AsSpan(tokenizer.Result));
                Elements = new TextElements(builder.Items.ToArray());
            }

            _isDirty = false;
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

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        (Pen as Animatable)?.ApplyAnimations(clock);
    }
}
