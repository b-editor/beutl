using System.ComponentModel.DataAnnotations;
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Styling;

namespace Beutl.Controls.PropertyEditors;

public class EnumEditor<TEnum> : EnumEditor
    where TEnum : struct, Enum
{
#pragma warning disable AVP1002 // AvaloniaProperty objects should not be owned by a generic type
    public static readonly DirectProperty<EnumEditor<TEnum>, TEnum> SelectedValueProperty =
        AvaloniaProperty.RegisterDirect<EnumEditor<TEnum>, TEnum>(
            nameof(SelectedValue),
            o => o.SelectedValue,
            (o, v) => o.SelectedValue = v,
            defaultBindingMode: BindingMode.TwoWay);
#pragma warning restore AVP1002 // AvaloniaProperty objects should not be owned by a generic type

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

    protected override Type StyleKeyOverride => typeof(EnumEditor);

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
                    ValueConfirmedEvent));
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
            PrevSelectedIndex = value;
            SetAndRaise(SelectedIndexProperty, ref _selectedIndex, value);
        }
    }

    protected ComboBox InnerComboBox { get; private set; }

    protected int PrevSelectedIndex { get; private set; }

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
        // EnumEditor.SelectedIndexが先に設定された場合、受け付けない
        if (InnerComboBox.SelectedIndex != PrevSelectedIndex)
        {
            // 必ず選択されている
            if (e.AddedItems.Count > 0)
            {
                RaiseEvent(new PropertyEditorValueChangedEventArgs<int>(InnerComboBox.SelectedIndex, PrevSelectedIndex, ValueConfirmedEvent));
            }
        }
    }
}
