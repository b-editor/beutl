using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;

using DynamicData;
using DynamicData.Binding;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public sealed class ProvidedChoiceEditorViewModel<T, TProvider> : ValueEditorViewModel<T?>
    where TProvider : IChoicesProvider
{
    private readonly ReadOnlyObservableCollection<string> _choices;
    private readonly IReadOnlyList<object> _originalChoices;

    public ProvidedChoiceEditorViewModel(IAbstractProperty<T?> property)
        : base(property)
    {
        _originalChoices = TProvider.GetChoices();
        IObservable<IChangeSet<object>>? observable;
        if (_originalChoices is INotifyCollectionChanged incc)
        {
            observable = _originalChoices.ToReadOnlyReactiveCollection(incc.ToCollectionChanged<object>())
                .ToObservableChangeSet();
        }
        else
        {
            observable = _originalChoices.AsObservableChangeSet();
        }

        observable.Cast(i => i?.ToString() ?? "null")
            .ObserveOnUIDispatcher()
            .Bind(out _choices)
            .Subscribe()
            .DisposeWith(Disposables);

        SelectedValue = Value
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(Disposables);

        SelectedIndex = Value.Select(v => _originalChoices.IndexOf(v))
            .ToReactiveProperty()
            .DisposeWith(Disposables);
    }

    public ReadOnlyReactivePropertySlim<T?> SelectedValue { get; }

    public ReactiveProperty<int> SelectedIndex { get; }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor editor && !Disposables.IsDisposed)
        {
            editor.Items = _choices;
            editor.Bind(EnumEditor.SelectedIndexProperty, SelectedIndex.ToBinding())
                .DisposeWith(Disposables);
            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<int> args)
        {
            int newIndex = args.NewValue;
            object? newValue = 0 <= newIndex && newIndex < _originalChoices.Count
                ? _originalChoices[newIndex]
                : null;

            SetValue(Value.Value, (T?)newValue ?? default);
        }
    }
}
