using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Avalonia;

using Beutl.Controls.PropertyEditors;
using Beutl.Framework;
using Beutl.Operation;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Editors;

public abstract class BaseEditorViewModel : IPropertyEditorContext
{
    protected CompositeDisposable Disposables = new();
    private bool _disposedValue;

    protected BaseEditorViewModel(IAbstractProperty property)
    {
        WrappedProperty = property;

        CorePropertyMetadata metadata = property.Property.GetMetadata<CorePropertyMetadata>(property.ImplementedType);
        Header = metadata.DisplayAttribute?.GetName() ?? property.Property.Name;

        IObservable<bool> hasAnimation = property is IAbstractAnimatableProperty anm
            ? anm.HasAnimation
            : Observable.Return(false);

        IObservable<bool>? observable;
        if (property.Property is IStaticProperty { CanWrite: false })
        {
            observable = Observable.Return(false);
        }
        else
        {
            observable = hasAnimation.Select(x => !x);
        }

        CanEdit = observable
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        IsReadOnly = observable
            .Not()
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);

        HasAnimation = hasAnimation
            .ToReadOnlyReactivePropertySlim()
            .AddTo(Disposables);
    }

    ~BaseEditorViewModel()
    {
        if (!_disposedValue)
            Dispose(false);
    }

    public IAbstractProperty WrappedProperty { get; }

    public bool CanReset => GetDefaultValue() != null;

    public string Header { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    public ReadOnlyReactivePropertySlim<bool> IsReadOnly { get; }

    public ReadOnlyReactivePropertySlim<bool> HasAnimation { get; }

    public bool IsAnimatable => WrappedProperty.Property.GetMetadata<CorePropertyMetadata>(WrappedProperty.ImplementedType).PropertyFlags.HasFlag(PropertyFlags.Animatable);

    public bool IsStylingSetter => WrappedProperty is IStylingSetterPropertyImpl;

    [AllowNull]
    public PropertyEditorExtension Extension { get; set; }

    public void Dispose()
    {
        if (!_disposedValue)
        {
            Dispose(true);
            _disposedValue = true;
            GC.SuppressFinalize(this);
        }
    }

    public abstract void Reset();

    public void WriteToJson(ref JsonNode json)
    {
    }

    public void ReadFromJson(JsonNode json)
    {
    }

    public virtual void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);
        if (visitor is PropertyEditor editor)
        {
            editor[!PropertyEditor.IsReadOnlyProperty] = IsReadOnly.ToBinding();
            editor.Header = Header;
        }
    }

    protected object? GetDefaultValue()
    {
        ICorePropertyMetadata metadata = WrappedProperty.Property.GetMetadata<ICorePropertyMetadata>(WrappedProperty.ImplementedType);
        return metadata.GetDefaultValue();
    }

    protected virtual void Dispose(bool disposing)
    {
        Disposables.Dispose();
    }
}

public abstract class BaseEditorViewModel<T> : BaseEditorViewModel
{
    protected BaseEditorViewModel(IAbstractProperty<T> property)
        : base(property)
    {
    }

    public new IAbstractProperty<T> WrappedProperty => (IAbstractProperty<T>)base.WrappedProperty;

    public sealed override void Reset()
    {
        if (GetDefaultValue() is { } defaultValue)
        {
            SetValue(WrappedProperty.GetValue(), (T?)defaultValue);
        }
    }

    public void SetValue(T? oldValue, T? newValue)
    {
        if (!EqualityComparer<T>.Default.Equals(oldValue, newValue))
        {
            CommandRecorder.Default.DoAndPush(new SetCommand(WrappedProperty, oldValue, newValue));
        }
    }

    private sealed class SetCommand : IRecordableCommand
    {
        private readonly IAbstractProperty<T> _setter;
        private readonly T? _oldValue;
        private readonly T? _newValue;

        public SetCommand(IAbstractProperty<T> setter, T? oldValue, T? newValue)
        {
            _setter = setter;
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public void Do()
        {
            _setter.SetValue(_newValue);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _setter.SetValue(_oldValue);
        }
    }
}
