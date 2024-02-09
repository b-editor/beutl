using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

using Beutl.Animation;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.TextFormatting;

namespace Beutl.Graphics.Shapes;

[DebuggerDisplay("{Text}")]
public class TextElement : Animatable, IAffectsRender
{
    public static readonly CoreProperty<FontWeight> FontWeightProperty;
    public static readonly CoreProperty<FontStyle> FontStyleProperty;
    public static readonly CoreProperty<FontFamily> FontFamilyProperty;
    public static readonly CoreProperty<float> SizeProperty;
    public static readonly CoreProperty<float> SpacingProperty;
    public static readonly CoreProperty<string> TextProperty;
    public static readonly CoreProperty<IBrush?> BrushProperty;
    public static readonly CoreProperty<IPen?> PenProperty;
    public static readonly CoreProperty<bool> IgnoreLineBreaksProperty;
    private FontWeight _fontWeight;
    private FontStyle _fontStyle;
    private FontFamily _fontFamily = FontFamily.Default;
    private float _size;
    private float _spacing;
    private string _text = string.Empty;
    private IBrush? _brush;
    private IPen? _pen = null;
    private bool _ignoreLineBreaks;

    static TextElement()
    {
        FontWeightProperty = ConfigureProperty<FontWeight, TextElement>(nameof(FontWeight))
            .Accessor(o => o.FontWeight, (o, v) => o.FontWeight = v)
            .DefaultValue(FontWeight.Regular)
            .Register();

        FontStyleProperty = ConfigureProperty<FontStyle, TextElement>(nameof(FontStyle))
            .Accessor(o => o.FontStyle, (o, v) => o.FontStyle = v)
            .DefaultValue(FontStyle.Normal)
            .Register();

        FontFamilyProperty = ConfigureProperty<FontFamily, TextElement>(nameof(FontFamily))
            .Accessor(o => o.FontFamily, (o, v) => o.FontFamily = v)
            .DefaultValue(FontFamily.Default)
            .Register();

        SizeProperty = ConfigureProperty<float, TextElement>(nameof(Size))
            .Accessor(o => o.Size, (o, v) => o.Size = v)
            .DefaultValue(0)
            .Register();

        SpacingProperty = ConfigureProperty<float, TextElement>(nameof(Spacing))
            .Accessor(o => o.Spacing, (o, v) => o.Spacing = v)
            .DefaultValue(0)
            .Register();

        TextProperty = ConfigureProperty<string, TextElement>(nameof(Text))
            .Accessor(o => o.Text, (o, v) => o.Text = v)
            .DefaultValue(string.Empty)
            .Register();

        BrushProperty = ConfigureProperty<IBrush?, TextElement>(nameof(Brush))
            .Accessor(o => o.Brush, (o, v) => o.Brush = v)
            .Register();

        PenProperty = ConfigureProperty<IPen?, TextElement>(nameof(Pen))
            .Accessor(o => o.Pen, (o, v) => o.Pen = v)
            .Register();

        IgnoreLineBreaksProperty = ConfigureProperty<bool, TextElement>(nameof(IgnoreLineBreaks))
            .Accessor(o => o.IgnoreLineBreaks, (o, v) => o.IgnoreLineBreaks = v)
            .DefaultValue(false)
            .Register();
    }

    public event EventHandler<RenderInvalidatedEventArgs>? Invalidated;

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

    public IBrush? Brush
    {
        get => _brush;
        set => SetAndRaise(BrushProperty, ref _brush, value);
    }

    public IPen? Pen
    {
        get => _pen;
        set => SetAndRaise(PenProperty, ref _pen, value);
    }

    public bool IgnoreLineBreaks
    {
        get => _ignoreLineBreaks;
        set => SetAndRaise(IgnoreLineBreaksProperty, ref _ignoreLineBreaks, value);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        switch (args.PropertyName)
        {
            case nameof(Brush) when args is CorePropertyChangedEventArgs<IBrush?> args1:
                if (args1.OldValue is IAffectsRender oldBrush)
                    oldBrush.Invalidated -= OnAffectsRenderInvalidated;

                if (args1.NewValue is IAffectsRender newBrush)
                    newBrush.Invalidated += OnAffectsRenderInvalidated;

                goto RaiseInvalidated;

            case nameof(Pen) when args is CorePropertyChangedEventArgs<IPen?> args2:
                if (args2.OldValue is IAffectsRender oldPen)
                    oldPen.Invalidated -= OnAffectsRenderInvalidated;

                if (args2.NewValue is IAffectsRender newPen)
                    newPen.Invalidated += OnAffectsRenderInvalidated;

                goto RaiseInvalidated;

            case nameof(FontWeight):
            case nameof(FontStyle):
            case nameof(FontFamily):
            case nameof(Size):
            case nameof(Spacing):
            case nameof(Text):
            case nameof(IgnoreLineBreaks):
            RaiseInvalidated:
                Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(args.PropertyName));
                break;

            default:
                break;
        }
    }

    private void OnAffectsRenderInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Invalidated?.Invoke(this, e);
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
                FormattedText item = span[ii];
                SetFields(item, new StringSpan(_text, prevIdx, (isReturnCode ? i : nextIdx) - prevIdx));
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

            if (isReturnCode || isLast)
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

    private void SetFields(FormattedText text, StringSpan s)
    {
        text.Weight = _fontWeight;
        text.Style = _fontStyle;
        text.Font = _fontFamily;
        text.Size = _size;
        text.Spacing = _spacing;
        text.Text = s;
        text.Brush = Brush;
        text.Pen = Pen;
    }
}

