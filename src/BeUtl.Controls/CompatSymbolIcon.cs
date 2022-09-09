using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;

using FluentIcons.Common;

namespace BeUtl.Controls;

public sealed class CompatSymbolIcon : FluentAvalonia.UI.Controls.FAIconElement
{
    private static readonly Typeface s_font = new(new FontFamily("avares://FluentIcons.Avalonia/Fonts#FluentSystemIcons-Resizable"));

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
        TextElement.FontSizeProperty.AddOwner<CompatSymbolIcon>();

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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextBlock.FontSizeProperty ||
            change.Property == SymbolProperty)
        {
            GenerateText();
            InvalidateMeasure();
        }
        else if (change.Property == TextBlock.ForegroundProperty)
        {
            GenerateText();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // Force invalidation of text for inherited properties now that we've attached to the tree
        if (_textLayout != null)
            GenerateText();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_textLayout == null)
            GenerateText();

        return _textLayout?.Bounds.Size ?? Size.Empty;
    }

    public override void Render(DrawingContext context)
    {
        if (_textLayout == null)
            GenerateText();

        var dstRect = new Rect(Bounds.Size);
        using (context.PushClip(dstRect))
        {
            var pt = new Point(dstRect.Center.X - _textLayout.Bounds.Width / 2,
                dstRect.Center.Y - _textLayout.Bounds.Height / 2);
            _textLayout.Draw(context, pt);
        }
    }

    private void GenerateText()
    {
        string glyph = char.ConvertFromUtf32(IsFilled ? ToFilledSymbol(Symbol) : (int)Symbol).ToString();

        _textLayout = new TextLayout(
            glyph,
            s_font,
            FontSize,
            Foreground,
            // Todo: Centerと比べる
            TextAlignment.Left);
    }

    private static int ToFilledSymbol(Symbol symbol)
    {
        var type = Type.GetType("FluentIcons.Common.Internals.FilledSymbol, FluentIcons.Common");
        return (int)Enum.Parse(type, Enum.GetName(typeof(Symbol), symbol));
    }
}
