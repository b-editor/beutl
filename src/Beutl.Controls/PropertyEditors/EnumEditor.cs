using System.ComponentModel.DataAnnotations;
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace Beutl.Controls.PropertyEditors;

public class EnumEditor<TEnum> : EnumEditor, IStyleable
    where TEnum : struct, Enum
{
    public static readonly DirectProperty<EnumEditor<TEnum>, TEnum> SelectedValueProperty =
        AvaloniaProperty.RegisterDirect<EnumEditor<TEnum>, TEnum>(
            nameof(SelectedValue),
            o => o.SelectedValue,
            (o, v) => o.SelectedValue = v,
            defaultBindingMode: BindingMode.TwoWay);

    private static readonly string[] s_enumStrings;
    private static readonly TEnum[] s_enumValues;
    private TEnum _selectedValue;

    static EnumEditor()
    {
        s_enumValues = Enum.GetValues<TEnum>();
        s_enumStrings = Enum.GetNames<TEnum>()
            .Select(typeof(TEnum).GetField)
            .Where(x => x != null)
            .Select(x => x.GetCustomAttribute<DisplayAttribute>()?.GetName() ?? x.Name)
            .ToArray();

        ItemsProperty.OverrideDefaultValue<EnumEditor<TEnum>>(s_enumStrings);
    }

    public TEnum SelectedValue
    {
        get => _selectedValue;
        set
        {
            if (SetAndRaise(SelectedValueProperty, ref _selectedValue, value))
            {
                SelectedIndex = Array.IndexOf(s_enumValues, value);
            }
        }
    }

    public override int SelectedIndex
    {
        get => base.SelectedIndex;
        set
        {
            value = Math.Clamp(value, 0, s_enumValues.Length - 1);
            base.SelectedIndex = value;
            SetAndRaise(SelectedValueProperty, ref _selectedValue, s_enumValues[value]);
        }
    }

    Type IStyleable.StyleKey => typeof(EnumEditor);

    protected override void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // EnumEditor.SelectedIndexが先に設定された場合、受け付けない
        if (InnerComboBox.SelectedIndex != PrevSelectedIndex)
        {
            // 必ず選択されている
            if (e.AddedItems.Count > 0)
            {
                int newIndex = Math.Clamp(InnerComboBox.SelectedIndex, 0, s_enumValues.Length - 1);
                int oldIndex = Math.Clamp(PrevSelectedIndex, 0, s_enumValues.Length - 1);
                RaiseEvent(new PropertyEditorValueChangedEventArgs<TEnum>(
                    s_enumValues[newIndex],
                    s_enumValues[oldIndex],
                    ValueChangedEvent));
            }
        }
    }
}

public class EnumEditor : PropertyEditor
{
    public static readonly StyledProperty<IReadOnlyList<string>> ItemsProperty =
        AvaloniaProperty.Register<EnumEditor, IReadOnlyList<string>>(nameof(Items));

    public static readonly DirectProperty<EnumEditor, int> SelectedIndexProperty =
        SelectingItemsControl.SelectedIndexProperty.AddOwner<EnumEditor>(
            o => o.SelectedIndex, (o, v) => o.SelectedIndex = v, defaultBindingMode: BindingMode.TwoWay);

    private int _selectedIndex;
    private int _prevSelectedIndex;
    private ComboBox _comboBox;
    private IDisposable _disposable;

    public IReadOnlyList<string> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public virtual int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            _prevSelectedIndex = value;
            SetAndRaise(SelectedIndexProperty, ref _selectedIndex, value);
        }
    }

    protected ComboBox InnerComboBox => _comboBox;

    protected int PrevSelectedIndex => _prevSelectedIndex;

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _disposable?.Dispose();
        base.OnApplyTemplate(e);
        _comboBox = e.NameScope.Get<ComboBox>("PART_InnerComboBox");

        _disposable = _comboBox.AddDisposableHandler(SelectingItemsControl.SelectionChangedEvent, OnComboBoxSelectionChanged);
        _prevSelectedIndex = SelectedIndex;
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
                PseudoClasses.Remove(":compact");
            }
        }

        return measured;
    }

    protected virtual void OnComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // EnumEditor.SelectedIndexが先に設定された場合、受け付けない
        if (_comboBox.SelectedIndex != _prevSelectedIndex)
        {
            // 必ず選択されている
            if (e.AddedItems.Count > 0)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<int>(_comboBox.SelectedIndex, _prevSelectedIndex, ValueChangedEvent));
            }
        }
    }
}
