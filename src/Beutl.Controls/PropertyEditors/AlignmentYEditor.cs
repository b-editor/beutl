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

[PseudoClasses(TopSelected, CenterSelected, BottomSelected)]
public class AlignmentYEditor : PropertyEditor
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
    private readonly CompositeDisposable _disposables = [];
    private AlignmentY _value;

    public AlignmentYEditor()
    {
        UpdatePseudoClasses();
    }

    public AlignmentY Value
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
        Button topBtn = e.NameScope.Get<Button>("PART_TopRadioButton");
        Button centerBtn = e.NameScope.Get<Button>("PART_CenterRadioButton");
        Button bottomBtn = e.NameScope.Get<Button>("PART_BottomRadioButton");
        topBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        centerBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        bottomBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
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
            AlignmentY? value = button.Name switch
            {
                "PART_TopRadioButton" => AlignmentY.Top,
                "PART_CenterRadioButton" => AlignmentY.Center,
                "PART_BottomRadioButton" => AlignmentY.Bottom,
                _ => null,
            };

            if (value.HasValue)
            {
                AlignmentY oldValue = Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentY>(value.Value, oldValue, ValueChangedEvent));
                Value = value.Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentY>(value.Value, oldValue, ValueConfirmedEvent));
            }
        }
    }

    private void UpdatePseudoClasses()
    {
        PseudoClasses.Remove(TopSelected);
        PseudoClasses.Remove(CenterSelected);
        PseudoClasses.Remove(BottomSelected);
        string add = Value switch
        {
            AlignmentY.Top => TopSelected,
            AlignmentY.Center => CenterSelected,
            AlignmentY.Bottom => BottomSelected,
            _ => null,
        };

        if (add != null)
        {
            PseudoClasses.Add(add);
        }
    }
}
