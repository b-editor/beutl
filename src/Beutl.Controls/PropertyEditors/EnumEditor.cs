using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace Beutl.Controls.PropertyEditors;

public class EnumItem(string? displayName, string? description, object value)
{
    public string DisplayName { get; } = displayName ?? string.Empty;

    public string? Description { get; } = description;

    public object Value { get; } = value;
}

public class EnumEditor : PropertyEditor
{
    public static readonly StyledProperty<IReadOnlyList<EnumItem>?> ItemsProperty =
        AvaloniaProperty.Register<EnumEditor, IReadOnlyList<EnumItem>?>(nameof(Items));

    public static readonly DirectProperty<EnumEditor, int> SelectedIndexProperty =
        SelectingItemsControl.SelectedIndexProperty.AddOwner<EnumEditor>(
            o => o.SelectedIndex, (o, v) => o.SelectedIndex = v, defaultBindingMode: BindingMode.TwoWay);

    private int _selectedIndex;
    private IDisposable? _disposable;

    public IReadOnlyList<EnumItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetAndRaise(SelectedIndexProperty, ref _selectedIndex, value);
    }

    private ComboBox InnerComboBox { get; set; } = null!;

    private int PrevSelectedIndex { get; set; }

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
        UpdateAutoCompact(availableSize);
        return measured;
    }

    protected virtual void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // A change that only clears the selection is ignored.
        if (e.AddedItems.Count > 0)
        {
            RaiseEvent(new PropertyEditorValueChangedEventArgs<int>(InnerComboBox.SelectedIndex, PrevSelectedIndex, ValueConfirmedEvent));
            PrevSelectedIndex = SelectedIndex;
        }
    }
}
