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
    private AlignmentX _value;
    private TextBlock _headerTextBlock;
    private StackPanel _stackPanel;
    private ContentPresenter _menuPresenter;
    private bool _shouldBeWrapped;

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
        base.OnApplyTemplate(e);
        Button leftBtn = e.NameScope.Get<Button>("PART_LeftButton");
        Button centerBtn = e.NameScope.Get<Button>("PART_CenterButton");
        Button rightBtn = e.NameScope.Get<Button>("PART_RightButton");
        leftBtn.Click += OnButtonClick;
        centerBtn.Click += OnButtonClick;
        rightBtn.Click += OnButtonClick;

        _headerTextBlock = e.NameScope.Get<TextBlock>("PART_HeaderTextBlock");
        _stackPanel = e.NameScope.Get<StackPanel>("PART_StackPanel");
        _menuPresenter = e.NameScope.Get<ContentPresenter>("PART_MenuContentPresenter");
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Size arranged = base.ArrangeOverride(finalSize);

        if (!UseCompact && _shouldBeWrapped)
        {
            Size headerSize = _headerTextBlock.DesiredSize;
            Size menuSize = _menuPresenter.DesiredSize;
            Size stackSize = _stackPanel.DesiredSize;

            // 横に並べたときavailableSizeをはみ出す
            _headerTextBlock.Arrange(new Rect(default, headerSize));

            double menuTop = new Rect(stackSize).CenterRect(new Rect(menuSize)).Top
                + headerSize.Height;

            _menuPresenter.Arrange(new Rect(new Point(arranged.Width - menuSize.Width, menuTop), menuSize));

            _stackPanel.Arrange(new Rect(new(0, headerSize.Height), stackSize));
            arranged = new Size(finalSize.Width, headerSize.Height + Math.Max(stackSize.Height, menuSize.Height));
        }

        return arranged;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!UseCompact && !double.IsInfinity(availableSize.Width))
        {
            Size measured = base.MeasureOverride(availableSize);
            _headerTextBlock.Measure(Size.Infinity);
            _stackPanel.Measure(Size.Infinity);
            _menuPresenter.Measure(Size.Infinity);

            Size headerSize = _headerTextBlock.DesiredSize;
            Size menuSize = _menuPresenter.DesiredSize;
            Size stackSize = _stackPanel.DesiredSize;

            double w = headerSize.Width + stackSize.Width + menuSize.Width;
            if (w > measured.Width)
            {
                _shouldBeWrapped = true;
                return new Size(measured.Width, headerSize.Height + Math.Max(stackSize.Height, menuSize.Height));
            }
        }

        _shouldBeWrapped = false;
        return base.MeasureOverride(availableSize);
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
