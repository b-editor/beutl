using System.Diagnostics;

using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Shapes;

[DebuggerDisplay("{Text}")]
public class TextElement : Drawable
{
    public static readonly CoreProperty<FontWeight> FontWeightProperty;
    public static readonly CoreProperty<FontStyle> FontStyleProperty;
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<float> SpacingProperty;
    public static readonly CoreProperty<string> TextProperty;
    public static readonly CoreProperty<Thickness> MarginProperty;
    public static readonly CoreProperty<bool> IgnoreLineBreaksProperty;
    private FontWeight _fontWeight;
    private FontStyle _fontStyle;
    private FontFamily _fontFamily;
    private float _size;
    private float _spacing;
    private string _text = string.Empty;
    private Thickness _margin;
    private bool _ignoreLineBreaks;
    private FormattedText _formattedText;

    static TextElement()
    {
        FontWeightProperty = ConfigureProperty<FontWeight, TextElement>(nameof(FontWeight))
            .Accessor(o => o.FontWeight, (o, v) => o.FontWeight = v)
            .Display(Strings.FontWeight)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(FontWeight.Regular)
            .SerializeName("font-weight")
            .Register();

        FontStyleProperty = ConfigureProperty<FontStyle, TextElement>(nameof(FontStyle))
            .Accessor(o => o.FontStyle, (o, v) => o.FontStyle = v)
            .Display(Strings.FontStyle)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(FontStyle.Normal)
            .SerializeName("font-style")
            .Register();

        FontFamilyProperty = ConfigureProperty<FontFamily, TextElement>(nameof(FontFamily))
            .Accessor(o => o.FontFamily, (o, v) => o.FontFamily = v)
            .Display(Strings.FontFamily)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(FontFamily.Default)
            .SerializeName("font-family")
            .Register();

        SizeProperty = ConfigureProperty<float, TextElement>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .Display(Strings.Size)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(0)
            .Minimum(0)
            .SerializeName("size")
            .Register();

        SpacingProperty = ConfigureProperty<float, TextElement>(nameof(Spacing))
            .Accessor(o => o.Spacing, (o, v) => o.Spacing = v)
            .Display(Strings.CharactorSpacing)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(0)
            .SerializeName("spacing")
            .Register();

        TextProperty = ConfigureProperty<string, TextElement>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .Display(Strings.Text)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(string.Empty)
            .SerializeName("text")
            .Register();

        MarginProperty = ConfigureProperty<Thickness, TextElement>(nameof(Margin))
            .Accessor(o => o.Margin, (o, v) => o.Margin = v)
            .Display(Strings.Margin)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(new Thickness())
            .SerializeName("margin")
            .Register();

        IgnoreLineBreaksProperty = ConfigureProperty<bool, TextElement>(nameof(IgnoreLineBreaks))
            .Accessor(o => o.IgnoreLineBreaks, (o, v) => o.IgnoreLineBreaks = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(false)
            .SerializeName("ignore-line-breaks")
            .Register();

        AffectsRender<TextElement>(
            FontWeightProperty,
            FontStyleProperty,
            FontFamilyProperty,
            SizeProperty,
            SpacingProperty,
            TextProperty,
            MarginProperty,
            IgnoreLineBreaksProperty);
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

    public FontFamily FontFamily
    {
        get => _fontFamily;
        set => SetAndRaise(FontFamilyProperty, ref _fontFamily, value);
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

    public bool IgnoreLineBreaks
    {
        get => _ignoreLineBreaks;
        set => SetAndRaise(IgnoreLineBreaksProperty, ref _ignoreLineBreaks, value);
    }

    internal int GetFormattedTexts(Span<FormattedText> span, bool startWithNewLine, out bool endWithNewLine)
    {
        int prevIdx = 0;
        int ii = 0;
        bool nextReturn = startWithNewLine;
        endWithNewLine = false;

        for (int i = 0; i < _text.Length; i++)
        {
            char c = _text[i];
            int nextIdx = i + 1;
            bool isReturnCode = c is '\n' or '\r';
            bool isLast = nextIdx == _text.Length;

            if (isReturnCode || isLast)
            {
                ref FormattedText item = ref span[ii];
                SetFields(ref item, new StringSpan(_text, prevIdx, (isReturnCode ? i : nextIdx) - prevIdx));
                item.BeginOnNewLine = nextReturn;
                nextReturn = !_ignoreLineBreaks && isReturnCode;

                ii++;
                if (c is '\r'
                    && nextIdx < _text.Length
                    && _text[nextIdx] is '\n')
                {
                    i++;
                    isLast = (nextIdx + 1) == _text.Length;
                }

                prevIdx = i + 1;

                if (!_ignoreLineBreaks && isReturnCode && isLast)
                {
                    endWithNewLine = true;
                }
            }
        }

        return ii;
    }

    internal int CountElements()
    {
        int count = 0;
        for (int i = 0; i < _text.Length; i++)
        {
            char c = _text[i];
            int nextIdx = i + 1;
            bool isReturnCode = c is '\n' or '\r';
            bool isLast = nextIdx == _text.Length;

            if (isReturnCode | isLast)
            {
                if (c is '\r'
                    && nextIdx < _text.Length
                    && _text[nextIdx] is '\n')
                {
                    i++;
                }

                count++;
            }
        }

        return count;
    }

    private void SetFields(ref FormattedText text, StringSpan s)
    {
        _formattedText.Weight = _fontWeight;
        _formattedText.Style = _fontStyle;
        _formattedText.Font = _fontFamily;
        _formattedText.Size = _size;
        _formattedText.Spacing = _spacing;
        _formattedText.Text = s;
        _formattedText.Brush = Foreground ?? Brushes.Transparent;

        text = _formattedText;
    }

    protected override Size MeasureCore(Size availableSize) => throw new NotImplementedException();

    protected override void OnDraw(ICanvas canvas) => throw new NotImplementedException();
}

