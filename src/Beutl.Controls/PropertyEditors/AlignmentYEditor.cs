using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

using Beutl.Media;

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
    private readonly CompositeDisposable _disposables = new();
    private AlignmentY _value;
    private TextBlock _headerTextBlock;
    private StackPanel _stackPanel;
    private ContentPresenter _menuPresenter;

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
        Button topBtn = e.NameScope.Get<Button>("PART_TopButton");
        Button centerBtn = e.NameScope.Get<Button>("PART_CenterButton");
        Button bottomBtn = e.NameScope.Get<Button>("PART_BottomButton");
        topBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        centerBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        bottomBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);

        _headerTextBlock = e.NameScope.Get<TextBlock>("PART_HeaderTextBlock");
        _stackPanel = e.NameScope.Get<StackPanel>("PART_StackPanel");
        _menuPresenter = e.NameScope.Get<ContentPresenter>("PART_MenuContentPresenter");
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        if (!double.IsInfinity(availableSize.Width))
        {
            _headerTextBlock.Measure(Size.Infinity);
            _stackPanel.Measure(Size.Infinity);
            _menuPresenter.Measure(Size.Infinity);

            Size headerSize = _headerTextBlock.DesiredSize;
            Size menuSize = _menuPresenter.DesiredSize;
            Size stackSize = _stackPanel.DesiredSize;

            double w = headerSize.Width + stackSize.Width + menuSize.Width;
            if (PseudoClasses.Contains(":compact"))
            {
                if (w < availableSize.Width)
                {
                    PseudoClasses.Remove(":compact");
                }
            }
            else if (w > availableSize.Width)
            {
                PseudoClasses.Add(":compact");
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
                "PART_TopButton" => AlignmentY.Top,
                "PART_CenterButton" => AlignmentY.Center,
                "PART_BottomButton" => AlignmentY.Bottom,
                _ => null,
            };

            if (value.HasValue)
            {
                AlignmentY oldValue = Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentY>(value.Value, oldValue, ValueChangingEvent));
                Value = value.Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentY>(value.Value, oldValue, ValueChangedEvent));
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
