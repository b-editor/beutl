using Avalonia;
using Avalonia.Data;

using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

internal class AlignmentXEditor : ThreeOptionRadioEditor<AlignmentX>
{
    public static readonly DirectProperty<AlignmentXEditor, AlignmentX> ValueProperty =
        AvaloniaProperty.RegisterDirect<AlignmentXEditor, AlignmentX>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public AlignmentX Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                UpdateCheckState();
            }
        }
    }

    protected override AlignmentX ButtonAValue => AlignmentX.Left;
    protected override AlignmentX ButtonBValue => AlignmentX.Center;
    protected override AlignmentX ButtonCValue => AlignmentX.Right;

    protected override void SetValue(AlignmentX value) => Value = value;
}

internal class TextAlignmentEditor : ThreeOptionRadioEditor<TextAlignment>
{
    public static readonly DirectProperty<TextAlignmentEditor, TextAlignment> ValueProperty =
        AvaloniaProperty.RegisterDirect<TextAlignmentEditor, TextAlignment>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public TextAlignment Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                UpdateCheckState();
            }
        }
    }

    protected override TextAlignment ButtonAValue => TextAlignment.Start;
    protected override TextAlignment ButtonBValue => TextAlignment.Center;
    protected override TextAlignment ButtonCValue => TextAlignment.End;

    protected override void SetValue(TextAlignment value) => Value = value;
}
