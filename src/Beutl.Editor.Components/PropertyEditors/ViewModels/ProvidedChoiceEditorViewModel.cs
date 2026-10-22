using System.Collections.ObjectModel;
using System.Collections.Specialized;

using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;

using DynamicData;
using DynamicData.Binding;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

public sealed class ProvidedChoiceEditorViewModel<T, TProvider> : PropertyEditorViewModel<T?, int>
    where TProvider : IChoicesProvider
{
    private readonly ReadOnlyObservableCollection<EnumItem> _choices;
    private readonly IReadOnlyList<object> _originalChoices;

    public ProvidedChoiceEditorViewModel(IPropertyAdapter<T?> property)
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

        observable.Cast(i => new EnumItem(i.ToString() ?? "null", null, i))
            .ObserveOnUIDispatcher()
            .Bind(out _choices)
            .Subscribe()
            .DisposeWith(Disposables);
    }

    protected override int ToEdit(T? value) => _originalChoices.IndexOf(value);

    protected override T? FromEdit(int edit)
    {
        object? value = 0 <= edit && edit < _originalChoices.Count ? _originalChoices[edit] : null;
        return (T?)value ?? default;
    }

    public override void ConfirmEdit(int oldValue, int newValue)
    {
        if (IsDisposed) return;
        SetValue(ShownValue.Value, FromEdit(newValue));
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        base.Accept(visitor);
        if (visitor is EnumEditor editor && !Disposables.IsDisposed)
        {
            editor.Items = _choices;
        }
    }
}
