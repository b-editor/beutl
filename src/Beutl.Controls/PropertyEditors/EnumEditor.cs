using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class EnumEditor : PropertyEditor
{
    public static readonly StyledProperty<IReadOnlyList<string>> ItemsProperty =
        AvaloniaProperty.Register<EnumEditor, IReadOnlyList<string>>(nameof(Items));

    public static readonly DirectProperty<EnumEditor, int> SelectedIndexProperty =
        SelectingItemsControl.SelectedIndexProperty.AddOwner<EnumEditor>(
            o => o.SelectedIndex, (o, v) => o.SelectedIndex = v, defaultBindingMode: BindingMode.TwoWay);

    private int _selectedIndex;
    private IDisposable _disposable;

    public IReadOnlyList<string> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public virtual int SelectedIndex
    {
        get => _selectedIndex;
        set => SetAndRaise(SelectedIndexProperty, ref _selectedIndex, value);
    }

    protected ComboBox InnerComboBox { get; private set; }

    protected int PrevSelectedIndex { get; set; }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposable?.Dispose();
        base.OnApplyTemplate(e);
        InnerComboBox = e.NameScope.Get<ComboBox>("PART_InnerComboBox");

        _disposable = InnerComboBox.AddDisposableHandler(SelectingItemsControl.SelectionChangedEvent, OnComboBoxSelectionChanged);
        PrevSelectedIndex = SelectedIndex;
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

    protected virtual void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 必ず選択されている
        if (e.AddedItems.Count > 0)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<int>(InnerComboBox.SelectedIndex, PrevSelectedIndex, ValueConfirmedEvent));
            PrevSelectedIndex = SelectedIndex;
        }
    }
}
