using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;

namespace Beutl.Controls;

/// <summary>What a <see cref="StatusLabel"/> says about the thing it labels, which decides its colors.</summary>
public enum StatusLabelKind
{
    Neutral,

    Accent,

    Success,

    Caution,

    Critical
}

/// <summary>A small label that shows the state of an item in a list or a row.</summary>
[PseudoClasses(AccentClass, SuccessClass, CautionClass, CriticalClass)]
public class StatusLabel : TemplatedControl
{
    private const string AccentClass = ":accent";
    private const string SuccessClass = ":success";
    private const string CautionClass = ":caution";
    private const string CriticalClass = ":critical";

    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusLabel, string?>(nameof(Text));

    public static readonly StyledProperty<StatusLabelKind> KindProperty =
        AvaloniaProperty.Register<StatusLabel, StatusLabelKind>(nameof(Kind));

    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusLabelKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
        {
            StatusLabelKind kind = change.GetNewValue<StatusLabelKind>();
            PseudoClasses.Set(AccentClass, kind == StatusLabelKind.Accent);
            PseudoClasses.Set(SuccessClass, kind == StatusLabelKind.Success);
            PseudoClasses.Set(CautionClass, kind == StatusLabelKind.Caution);
            PseudoClasses.Set(CriticalClass, kind == StatusLabelKind.Critical);
        }
    }
}
