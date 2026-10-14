using Avalonia;
using Avalonia.Data;

using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

public class AlignmentXEditor : ThreeOptionRadioEditor<AlignmentX>
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
