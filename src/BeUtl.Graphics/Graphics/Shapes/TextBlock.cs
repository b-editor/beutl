using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BeUtl.Media;

using FormattedText = BeUtl.Media.TextFormatting.FormattedText_;

namespace BeUtl.Graphics.Shapes;

public class TextBlock : Drawable
{
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<FontWeight> FontWeightProperty;
    public static readonly CoreProperty<FontStyle> FontStyleProperty;
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<float> SpacingProperty;
    public static readonly CoreProperty<string> TextProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    public static readonly CoreProperty<TextElements> ElementsProperty;
    private FontFamily _fontFamily;
    private FontWeight _fontWeight;
    private FontStyle _fontStyle;
    private float _size;
    private float _spacing;
    private string _text = string.Empty;
    private Thickness _margin;
    private TextElements _elements = TextElements.Empty;

    static TextBlock()
    {
        FontWeightProperty = ConfigureProperty<FontWeight, TextBlock>(nameof(FontWeight))
            .Accessor(o => o.FontWeight, (o, v) => o.FontWeight = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(FontWeight.Regular)
            .SerializeName("font-weight")
            .Register();

        FontStyleProperty = ConfigureProperty<FontStyle, TextBlock>(nameof(FontStyle))
            .Accessor(o => o.FontStyle, (o, v) => o.FontStyle = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(FontStyle.Normal)
            .SerializeName("font-style")
            .Register();

        FontFamilyProperty = ConfigureProperty<FontFamily, TextBlock>(nameof(FontFamily))
            .Accessor(o => o.FontFamily, (o, v) => o.FontFamily = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(FontFamily.Default)
            .SerializeName("font-family")
            .Register();

        SizeProperty = ConfigureProperty<float, TextBlock>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(0)
            .SerializeName("size")
            .Register();

        SpacingProperty = ConfigureProperty<float, TextBlock>(nameof(Spacing))
            .Accessor(o => o.Spacing, (o, v) => o.Spacing = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(0)
            .SerializeName("spacing")
            .Register();

        TextProperty = ConfigureProperty<string, TextBlock>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(string.Empty)
            .SerializeName("text")
            .Register();

        MarginProperty = ConfigureProperty<Thickness, TextBlock>(nameof(Margin))
            .Accessor(o => o.Margin, (o, v) => o.Margin = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(new Thickness())
            .SerializeName("margin")
            .Register();

        ElementsProperty = ConfigureProperty<TextElements, TextBlock>(nameof(Elements))
            .Accessor(o => o.Elements, (o, v) => o.Elements = v)
            .PropertyFlags(PropertyFlags.KnownFlags_1)
            .DefaultValue(TextElements.Empty)
            .SerializeName("elements")
            .Register();

        AffectsRender<TextBlock>(ElementsProperty);
    }

    public FontFamily FontFamily
    {
        get => _fontFamily;
        set => SetAndRaise(FontFamilyProperty, ref _fontFamily, value);
    }

    public FontWeight FontWeight
    {
        get => _fontWeight;
        set => SetAndRaise(FontWeightProperty, ref _fontWeight, value);
    }

    public FontStyle FontStyle
    {
        get => _fontStyle;
        set => SetAndRaise(FontStyleProperty, ref _fontStyle, value);
    }

    public float Size
    {
        get => _size;
        set => SetAndRaise(SizeProperty, ref _size, value);
    }

    public float Spacing
    {
        get => _spacing;
        set => SetAndRaise(SpacingProperty, ref _spacing, value);
    }

    public string Text
    {
        get => _text;
        set => SetAndRaise(TextProperty, ref _text, value);
    }

    public Thickness Margin
    {
        get => _margin;
        set => SetAndRaise(MarginProperty, ref _margin, value);
    }

    public TextElements Elements
    {
        get => _elements;
        set => SetAndRaise(ElementsProperty, ref _elements, value);
    }

    protected override Size MeasureCore(Size availableSize)
    {
        float width = 0;
        float height = 0;

        foreach (Span<FormattedText> line in Elements.Lines)
        {
            Size bounds = MeasureLine(line);
            width = MathF.Max(bounds.Width, width);
            height += bounds.Height;
        }

        return new Size(width, height);
    }

    protected override void OnDraw(ICanvas canvas)
    {
        float prevBottom = 0;
        foreach (Span<FormattedText> line in Elements.Lines)
        {
            Size lineBounds = MeasureLine(line);
            float ascent = MinAscent(line);

            using (canvas.PushTransform(Matrix.CreateTranslation(0, prevBottom - ascent)))
            {
                float prevRight = 0;
                foreach (FormattedText item in line)
                {
                    canvas.Translate(new(prevRight, 0));
                    Size elementBounds = item.Bounds;

                    canvas.DrawText(item);

                    prevRight = elementBounds.Width + item.Margin.Right;
                }
            }

            prevBottom += lineBounds.Height;
        }
    }

    private static Size MeasureLine(Span<FormattedText> items)
    {
        float width = 0;
        float height = 0;

        foreach (FormattedText element in items)
        {
            Size bounds = element.Bounds;
            width += bounds.Width;
            width += element.Margin.Left + element.Margin.Right;

            height = MathF.Max(bounds.Height + element.Margin.Top + element.Margin.Bottom, height);
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
