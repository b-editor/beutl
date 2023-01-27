using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

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
    private readonly CompositeDisposable _disposables = new();
    private AlignmentX _value;
    private TextBlock _headerTextBlock;
    private StackPanel _stackPanel;
    private ContentPresenter _menuPresenter;

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
        Button leftBtn = e.NameScope.Get<Button>("PART_LeftButton");
        Button centerBtn = e.NameScope.Get<Button>("PART_CenterButton");
        Button rightBtn = e.NameScope.Get<Button>("PART_RightButton");
        leftBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        centerBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
            .DisposeWith(_disposables);
        rightBtn.AddDisposableHandler(Button.ClickEvent, OnButtonClick)
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
            AlignmentX? value = button.Name switch
            {
                "PART_LeftButton" => AlignmentX.Left,
                "PART_CenterButton" => AlignmentX.Center,
                "PART_RightButton" => AlignmentX.Right,
                _ => null,
            };

            if (value.HasValue)
            {
                AlignmentX oldValue = Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentX>(value.Value, oldValue, ValueChangingEvent));
                Value = value.Value;
                RaiseEvent(new PropertyEditorValueChangedEventArgs<AlignmentX>(value.Value, oldValue, ValueChangedEvent));
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
