using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Beutl.Animation.Easings;
using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

public sealed class EasingItem
{
    // The preview defaults to a new instance; a type whose default is uninformative
    // (a spline starts out linear) can pass a representative curve instead.
    public EasingItem(string? displayName, string? description, Type type, Easing? preview = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsAssignableTo(typeof(Easing)) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new ArgumentException("The type must be a concrete Easing with a public parameterless constructor.", nameof(type));
        }

        DisplayName = displayName ?? type.Name;
        Description = description;
        Type = type;
        Preview = preview ?? CreateInstance();
    }

    public string DisplayName { get; }

    public string? Description { get; }

    public Type Type { get; }

    public Easing Preview { get; }

    public Easing CreateInstance() => (Easing)Activator.CreateInstance(Type)!;
}

public class EasingEditor : PropertyEditor
{
    public static readonly StyledProperty<Easing?> ValueProperty =
        AvaloniaProperty.Register<EasingEditor, Easing?>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IReadOnlyList<EasingItem>?> ItemsProperty =
        AvaloniaProperty.Register<EasingEditor, IReadOnlyList<EasingItem>?>(nameof(Items));

    private readonly CompositeDisposable _templateDisposables = [];
    private ComboBox? _comboBox;
    private bool _syncingSelection;

    public Easing? Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public IReadOnlyList<EasingItem>? Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        _templateDisposables.Clear();
        base.OnApplyTemplate(e);

        _comboBox = e.NameScope.Find<ComboBox>("PART_InnerComboBox");
        _comboBox?.AddDisposableHandler(SelectingItemsControl.SelectionChangedEvent, OnComboBoxSelectionChanged)
            .DisposeWith(_templateDisposables);

        if (e.NameScope.Find<EasingCurveEditor>("PART_CurveEditor") is { } curveEditor)
        {
            curveEditor.Editing += OnCurveEditing;
            curveEditor.Edited += OnCurveEdited;
            Disposable.Create(() =>
                {
                    curveEditor.Editing -= OnCurveEditing;
                    curveEditor.Edited -= OnCurveEdited;
                })
                .DisposeWith(_templateDisposables);
        }

        SyncSelection();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        Size measured = base.MeasureOverride(availableSize);
        UpdateAutoCompact(availableSize);
        return measured;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == ItemsProperty)
        {
            SyncSelection();
        }
    }

    private void SyncSelection()
    {
        if (_comboBox == null) return;

        _syncingSelection = true;
        try
        {
            IReadOnlyList<EasingItem>? items = Items;
            if (!ReferenceEquals(_comboBox.ItemsSource, items))
            {
                _comboBox.ItemsSource = items;
            }

            Type? type = Value?.GetType();
            int index = -1;
            if (items != null && type != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Type == type)
                    {
                        index = i;
                        break;
                    }
                }
            }

            _comboBox.SelectedIndex = index;
            // An easing outside the offered items (e.g. from an unloaded extension) is still named.
            _comboBox.PlaceholderText = index < 0 && type != null ? TypeDisplayHelpers.GetLocalizedName(type) : null;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private void OnComboBoxSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || _comboBox?.SelectedItem is not EasingItem item) return;

        Easing? oldValue = Value;
        if (oldValue?.GetType() == item.Type) return;

        Easing newValue = item.CreateInstance();
        Value = newValue;
        RaiseEvent(new PropertyEditorValueChangedEventArgs<Easing?>(newValue, oldValue, ValueConfirmedEvent));
    }

    private void OnCurveEditing(object? sender, EasingCurveEditedEventArgs e)
    {
        Value = e.NewValue;
        RaiseEvent(new PropertyEditorValueChangedEventArgs<Easing?>(e.NewValue, e.OldValue, ValueChangedEvent));
    }

    private void OnCurveEdited(object? sender, EasingCurveEditedEventArgs e)
    {
        Value = e.NewValue;
        RaiseEvent(new PropertyEditorValueChangedEventArgs<Easing?>(e.NewValue, e.OldValue, ValueConfirmedEvent));
    }
}
