using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

using FluentIcons.Common;

namespace BeUtl.Controls;

public sealed class CompatSymbolIcon : FluentAvalonia.UI.Controls.IconElement
{
    private static readonly Typeface s_font
            = new(new FontFamily("avares://FluentIcons.Avalonia/Fonts#FluentSystemIcons-Resizable"));

    private TextLayout _textLayout;

    static CompatSymbolIcon()
    {
        FontSizeProperty.OverrideDefaultValue<CompatSymbolIcon>(18d);
    }

    public static readonly StyledProperty<Symbol> SymbolProperty =
        AvaloniaProperty.Register<CompatSymbolIcon, Symbol>(nameof(Symbol));

    public static readonly StyledProperty<bool> IsFilledProperty =
        AvaloniaProperty.Register<CompatSymbolIcon, bool>(nameof(IsFilled));

    public static readonly StyledProperty<double> FontSizeProperty =
            TextBlock.FontSizeProperty.AddOwner<CompatSymbolIcon>();

    public Symbol Symbol
    {
        get => GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public bool IsFilled
    {
        get => GetValue(IsFilledProperty);
        set => SetValue(IsFilledProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    protected override void OnPropertyChanged<T>(AvaloniaPropertyChangedEventArgs<T> change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextBlock.FontSizeProperty ||
            change.Property == SymbolProperty)
        {
            OnInvalidateText();
            InvalidateMeasure();
        }
        else if (change.Property == TextBlock.ForegroundProperty)
        {
            OnInvalidateText();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_textLayout == null)
            OnInvalidateText();

        return _textLayout.Size;
    }

    public override void Render(DrawingContext context)
    {
        if (_textLayout == null)
            OnInvalidateText();

        var canvas = new Rect(Bounds.Size);
        using (context.PushClip(canvas))
        using (context.PushPreTransform(Matrix.CreateTranslation(
            canvas.Center.X - _textLayout.Size.Width / 2,
            canvas.Center.Y - _textLayout.Size.Height / 2)))
        {
            _textLayout.Draw(context);
        }
    }

    private void OnInvalidateText()
    {
        string glyph = char.ConvertFromUtf32(IsFilled ? ToFilledSymbol(Symbol) : (int)Symbol).ToString();

        _textLayout = new TextLayout(
            glyph,
            s_font,
            FontSize,
            Foreground,
            TextAlignment.Center);
    }

    private static int ToFilledSymbol(Symbol symbol)
    {
        var type = Type.GetType("FluentIcons.Common.Internals.FilledSymbol, FluentIcons.Common");
        return (int)Enum.Parse(type, Enum.GetName(typeof(Symbol), symbol));
    }
}
