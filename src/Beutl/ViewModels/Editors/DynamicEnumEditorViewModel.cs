using System.Collections;
using System.Collections.Specialized;

using Avalonia;

using Beutl.Controls.PropertyEditors;

using DynamicData;

using Reactive.Bindings;

namespace Beutl.ViewModels.Editors;

public sealed class DynamicEnumEditorViewModel<T> : ValueEditorViewModel<T?>
    where T : IDynamicEnum
{
    private IDisposable? _valuesSubscription;

    public DynamicEnumEditorViewModel(IAbstractProperty<T?> property)
        : base(property)
    {
        Value.Subscribe(value =>
            {
                _valuesSubscription?.Dispose();
                _valuesSubscription = null;

                Items.Replace([.. value?.Values?.Select(v => v.DisplayName) ?? []]);
                if (value?.Values != null)
                {
                    value.Values.CollectionChanged += OnValuesCollectionChanged;
                    _valuesSubscription = Disposable.Create(value.Values, v => v.CollectionChanged -= OnValuesCollectionChanged);
                }
            })
            .DisposeWith(Disposables);

        SelectedValue = Value.Select(v => v?.SelectedValue)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        SelectedIndex = Value.Select(v => v?.Values.IndexOf(v.SelectedValue) ?? -1)
            .ToReactiveProperty()
            .DisposeWith(Disposables);
    }

    private void OnValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        void Add(int index, IList items)
        {
            foreach (IDynamicEnumValue item in items)
            {
                Items.Insert(index++, item.DisplayName);
            }
        }

        void Remove(int index, IList items)
        {
            for (int i = items.Count - 1; i >= 0; --i)
            {
                Items.RemoveAt(index + i);
            }
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Remove:
                Remove(e.OldStartingIndex, e.OldItems!);
                break;

            case NotifyCollectionChangedAction.Move:
            case NotifyCollectionChangedAction.Replace:
                Remove(e.OldStartingIndex, e.OldItems!);
                Add(e.NewStartingIndex, e.NewItems!);
                break;

            case NotifyCollectionChangedAction.Reset:
                Items.Clear();
                break;
        }

        SelectedIndex.Value = Value.Value?.Values.IndexOf(Value.Value.SelectedValue) ?? -1;
    }

    public CoreList<string> Items { get; } = [];

    public ReadOnlyReactivePropertySlim<IDynamicEnumValue?> SelectedValue { get; }

    public ReactiveProperty<int> SelectedIndex { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor editor)
        {
            editor.Items = Items;
            editor[!EnumEditor.SelectedIndexProperty] = SelectedIndex.ToBinding();
            editor.ValueConfirmed += OnValueConfirmed;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<int> args)
        {
            int newIndex = args.NewValue;
            IDynamicEnumValue? newValue = 0 <= newIndex && newIndex < Items.Count
                ? Value.Value?.Values[newIndex]
                : null;

            SetValue(Value.Value, (T?)(Value.Value?.WithNewValue(newValue)));
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _valuesSubscription?.Dispose();
        _valuesSubscription = null;
    }
}
