using Avalonia;
using Avalonia.Controls.Metadata;
using Avalonia.Data;

using Beutl.Media;

namespace Beutl.Controls.PropertyEditors;

[PseudoClasses(TopSelected, CenterSelected, BottomSelected)]
public class AlignmentYEditor : ThreeOptionRadioEditor<AlignmentY>
{
    public static readonly DirectProperty<AlignmentYEditor, AlignmentY> ValueProperty =
        AvaloniaProperty.RegisterDirect<AlignmentYEditor, AlignmentY>(
            nameof(Value),
            o => o.Value,
            (o, v) => o.Value = v,
            defaultBindingMode: BindingMode.TwoWay);

    private const string TopSelected = ":top-selected";
    private const string CenterSelected = ":center-selected";
    private const string BottomSelected = ":bottom-selected";

    public AlignmentYEditor()
    {
        UpdatePseudoClassesAndCheckState();
    }

    public AlignmentY Value
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

    protected override string ButtonAName => "PART_TopRadioButton";
    protected override string ButtonBName => "PART_CenterRadioButton";
    protected override string ButtonCName => "PART_BottomRadioButton";

    protected override AlignmentY ButtonAValue => AlignmentY.Top;
    protected override AlignmentY ButtonBValue => AlignmentY.Center;
    protected override AlignmentY ButtonCValue => AlignmentY.Bottom;

    protected override string PseudoClassA => TopSelected;
    protected override string PseudoClassB => CenterSelected;
    protected override string PseudoClassC => BottomSelected;

    protected override void SetValue(AlignmentY value) => Value = value;
}
