using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Threading;

using Beutl.Media;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

[PseudoClasses(LeftSelected, CenterSelected, RightSelected)]
public class AlignmentXEditor : PropertyEditor
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
    private readonly CompositeDisposable _disposables = [];
    private AlignmentX _value;
    private RadioButton _leftButton;
    private RadioButton _centerButton;
    private RadioButton _rightButton;

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

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();
        base.OnApplyTemplate(e);
        _leftButton = e.NameScope.Get<RadioButton>("PART_LeftRadioButton");
        _centerButton = e.NameScope.Get<RadioButton>("PART_CenterRadioButton");
        _rightButton = e.NameScope.Get<RadioButton>("PART_RightRadioButton");

        _leftButton.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        _centerButton.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        _rightButton.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        if (!double.IsInfinity(availableSize.Width))
        {
            if (availableSize.Width <= 224)
            {
                if (!PseudoClasses.Contains(":compact"))
                {
                    PseudoClasses.Add(":compact");
                }
            }
            else
            {
                if (EditorStyle != PropertyEditorStyle.Compact)
                    PseudoClasses.Remove(":compact");
            }
        }

        return measured;
    }

    private void OnButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            AlignmentX? value = button.Name switch
            {
                "PART_LeftRadioButton" => AlignmentX.Left,
                "PART_CenterRadioButton" => AlignmentX.Center,
                "PART_RightRadioButton" => AlignmentX.Right,
                _ => null,
            };

            if (value.HasValue)
            {
                AlignmentX oldValue = Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentX>(value.Value, oldValue, ValueChangedEvent));
                Value = value.Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentX>(value.Value, oldValue, ValueConfirmedEvent));
            }
        }
    }

    private void UpdatePseudoClassesAndCheckState()
    {
        PseudoClasses.Remove(LeftSelected);
        PseudoClasses.Remove(CenterSelected);
        PseudoClasses.Remove(RightSelected);
        string pseudoClass = null;
        RadioButton radioButton = null;

        switch (Value)
        {
            case AlignmentX.Left:
                pseudoClass = LeftSelected;
                radioButton=_leftButton;
                break;

            case AlignmentX.Center:
                pseudoClass = CenterSelected;
                radioButton = _centerButton;
                break;

            case AlignmentX.Right:
                pseudoClass = RightSelected;
                radioButton = _rightButton;
                break;
        }

        if (radioButton != null)
        {
            radioButton.IsChecked = true;
        }

        if (pseudoClass != null)
        {
            PseudoClasses.Add(pseudoClass);
        }
    }
}
