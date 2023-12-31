using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

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

    public AlignmentXEditor()
    {
        UpdatePseudoClasses();
    }

    public AlignmentX Value
    {
        get => _value;
        set
        {
            if (SetAndRaise(ValueProperty, ref _value, value))
            {
                UpdatePseudoClasses();
            }
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposables.Clear();

        base.OnApplyTemplate(e);
        Button leftBtn = e.NameScope.Get<Button>("PART_LeftRadioButton");
        Button centerBtn = e.NameScope.Get<Button>("PART_CenterRadioButton");
        Button rightBtn = e.NameScope.Get<Button>("PART_RightRadioButton");
        leftBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        centerBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        rightBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
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

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Remove(LeftSelected);
        PseudoClasses.Remove(CenterSelected);
        PseudoClasses.Remove(RightSelected);
        string add = Value switch
        {
            AlignmentX.Left => LeftSelected,
            AlignmentX.Center => CenterSelected,
            AlignmentX.Right => RightSelected,
            _ => null,
        };

        if (add != null)
        {
            PseudoClasses.Add(add);
        }
    }
}
