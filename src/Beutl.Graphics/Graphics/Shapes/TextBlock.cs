using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;

using DynamicData;

namespace Beutl.Graphics.Shapes;

public class TextBlock : Drawable
{
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<FontWeight> FontWeightProperty;
    public static readonly CoreProperty<FontStyle> FontStyleProperty;
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<float> SpacingProperty;
    public static readonly CoreProperty<string> TextProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    public static readonly CoreProperty<TextElements?> ElementsProperty;
    private FontFamily _fontFamily = FontFamily.Default;
    private FontWeight _fontWeight = FontWeight.Regular;
    private FontStyle _fontStyle = FontStyle.Normal;
    private float _size;
    private float _spacing;
    private string _text = string.Empty;
    private Thickness _margin;
    private TextElements? _elements;

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

        FontFamilyProperty = ConfigureProperty<FontFamily, TextBlock>(nameof(FontFamily))
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

        TextProperty = ConfigureProperty<string, TextBlock>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .DefaultValue(string.Empty)
            .Register();

        MarginProperty = ConfigureProperty<Thickness, TextBlock>(nameof(Margin))
            .Accessor(o => o.Margin, (o, v) => o.Margin = v)
            .DefaultValue(new Thickness())
            .Register();

        ElementsProperty = ConfigureProperty<TextElements?, TextBlock>(nameof(Elements))
            .Accessor(o => o.Elements, (o, v) => o.Elements = v)
            .Register();

        AffectsRender<TextBlock>(ElementsProperty);
    }

    public TextBlock()
    {
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
    public FontFamily FontFamily
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
    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    [Display(Name = nameof(Strings.Margin), ResourceType = typeof(Strings))]
    public Thickness Margin
    {
        get => _margin;
        set => SetAndRaise(MarginProperty, ref _margin, value);
    }

    [NotAutoSerialized]
    public TextElements? Elements
    {
        get => _elements;
        set => SetAndRaise(ElementsProperty, ref _elements, value);
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue("elements", out JsonNode? elmsNode)
            && elmsNode is JsonArray elnsArray)
        {
            var array = new TextElement[elnsArray.Count];
            for (int i = 0; i < elnsArray.Count; i++)
            {
                if (elnsArray[i] is JsonObject elmNode)
                {
                    var elm = new TextElement();
                    elm.ReadFromJson(elmNode);
                    array[i] = elm;
                }
            }

            Elements = new TextElements(array);
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        if (_elements != null)
        {
            var array = new JsonArray(_elements.Count);
            for (int i = 0; i < _elements.Count; i++)
            {
                var node = new JsonObject();
                _elements[i].WriteToJson(node);
                array[i] = node;
            }

            json["elements"] = array;
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        float width = 0;
        float height = 0;

        if (_elements != null)
        {
            foreach (Span<FormattedText> line in _elements.Lines)
            {
                Size bounds = MeasureLine(line);
                width = MathF.Max(bounds.Width, width);
                height += bounds.Height;
            }
        }

        return new Size(width, height);
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (_elements != null)
        {
            float prevBottom = 0;
            foreach (Span<FormattedText> line in _elements.Lines)
            {
                Size lineBounds = MeasureLine(line);
                float ascent = MinAscent(line);

                using (canvas.PushTransform(Matrix.CreateTranslation(0, prevBottom - ascent)))
                {
                    float prevRight = 0;
                    foreach (FormattedText item in line)
                    {
                        if (item.Text.Length > 0)
                        {
                            canvas.Transform = Matrix.CreateTranslation(prevRight, 0) * canvas.Transform;
                            Size elementBounds = item.Bounds;

                            using (item.Brush != null ? canvas.PushFillBrush(item.Brush) : default)
                                canvas.DrawText(item);

                            prevRight = elementBounds.Width + item.Margin.Right;
                        }
                    }
                }

                prevBottom += lineBounds.Height;
            }
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(Elements))
        {
            if (args is CorePropertyChangedEventArgs<TextElements> typedargs)
            {
                if (typedargs.OldValue != null)
                {
                    HierarchicalChildren.RemoveMany(typedargs.OldValue);
                }

                if (typedargs.NewValue != null)
                {
                    HierarchicalChildren.AddRange(typedargs.NewValue);
                }
            }
        }

        if (args.PropertyName is nameof(Text) or nameof(Size) or nameof(FontFamily) or nameof(FontStyle) or nameof(FontWeight) or nameof(Foreground) or nameof(Spacing) or nameof(Margin))
        {
            OnUpdateText();
        }
    }

    private void OnUpdateText()
    {
        var tokenizer = new FormattedTextTokenizer(_text);
        tokenizer.Tokenize();
        var options = new FormattedTextInfo(
            Typeface: new Typeface(_fontFamily, _fontStyle, _fontWeight),
            Size: _size,
            Brush: (Foreground as IMutableBrush)?.ToImmutable(),
            Space: _spacing,
            Margin: _margin);

        var builder = new TextElementsBuilder(options);
        builder.AppendTokens(CollectionsMarshal.AsSpan(tokenizer.Result));
        Elements = new TextElements(builder.Items.ToArray());
    }

    private static Size MeasureLine(Span<FormattedText> items)
    {
        float width = 0;
        float height = 0;

        foreach (FormattedText element in items)
        {
            if (element.Text.Length > 0)
            {
                Size bounds = element.Bounds;
                width += bounds.Width;
                width += element.Margin.Left + element.Margin.Right;

                height = MathF.Max(bounds.Height + element.Margin.Top + element.Margin.Bottom, height);
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
}
