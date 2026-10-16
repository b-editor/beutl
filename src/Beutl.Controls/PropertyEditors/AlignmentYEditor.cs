using Avalonia;
using Avalonia.Data;

using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

internal class AlignmentYEditor : ThreeOptionRadioEditor<AlignmentY>
{
    public static readonly DirectProperty<AlignmentYEditor, AlignmentY> ValueProperty =
        AvaloniaProperty.RegisterDirect<AlignmentYEditor, AlignmentY>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    public AlignmentY Value
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

    protected override AlignmentY ButtonAValue => AlignmentY.Top;
    protected override AlignmentY ButtonBValue => AlignmentY.Center;
    protected override AlignmentY ButtonCValue => AlignmentY.Bottom;

    protected override void SetValue(AlignmentY value) => Value = value;
}
