using Avalonia;
using Avalonia.Controls.Metadata;
using Avalonia.Data;

using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

[PseudoClasses(LeftSelected, CenterSelected, RightSelected)]
public class AlignmentXEditor : ThreeOptionRadioEditor<AlignmentX>
{
    public static readonly DirectProperty<AlignmentXEditor, AlignmentX> ValueProperty =
        AvaloniaProperty.RegisterDirect<AlignmentXEditor, AlignmentX>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private const string LeftSelected = ":left-selected";
    private const string CenterSelected = ":center-selected";
    private const string RightSelected = ":right-selected";

    public AlignmentXEditor()
    {
        UpdatePseudoClassesAndCheckState();
    }

    public AlignmentX Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                UpdatePseudoClassesAndCheckState();
            }
        }
    }

    protected override string ButtonAName => "PART_LeftRadioButton";
    protected override string ButtonBName => "PART_CenterRadioButton";
    protected override string ButtonCName => "PART_RightRadioButton";

    protected override AlignmentX ButtonAValue => AlignmentX.Left;
    protected override AlignmentX ButtonBValue => AlignmentX.Center;
    protected override AlignmentX ButtonCValue => AlignmentX.Right;

    protected override string PseudoClassA => LeftSelected;
    protected override string PseudoClassB => CenterSelected;
    protected override string PseudoClassC => RightSelected;

    protected override void SetValue(AlignmentX value) => Value = value;
}
