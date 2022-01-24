using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeUtl.ProjectSystem;

public interface IPropertyInstance : IJsonSerializable, INotifyPropertyChanged, ILogicalElement
{
    CoreProperty Property { get; set; }

    Element Parent { get; set; }

    object? Value { get; }

    void SetProperty();

    IObservable<Unit> GetObservable();
}

public class PropertyInstance<T> : IPropertyInstance
{
    private CoreProperty<T>? _property;
    private T? _value;
    private Element? _parent;

    public PropertyInstance()
    {
    }

    public PropertyInstance(CoreProperty<T> property)
    {
        Property = property;
        Value = property.GetMetadata<CorePropertyMetadata<T>>(property.OwnerType).DefaultValue;
    }

    public CoreProperty<T> Property
    {
        get => _property ?? throw new Exception("The property is not set.");
        set
        {
            if (_property != value)
            {
                _property = value ?? throw new ArgumentNullException(nameof(value));
                OnPropertyChanged();
            }
        }
    }

    public T? Value
    {
        get => _value;
        set
        {
            if (!EqualityComparer<T>.Default.Equals(_value, value))
            {
                _value = value;
                OnPropertyChanged();
            }
        }
    }

    CoreProperty IPropertyInstance.Property
    {
        get => Property;
        set => Property = (CoreProperty<T>)value;
    }

    public Element Parent
    {
        get => _parent ?? throw new Exception("The property is not set.");
        set
        {
            if (_parent != value)
            {
                _parent = value ?? throw new ArgumentNullException(nameof(value));
                OnPropertyChanged();
            }
        }
    }

    ILogicalElement? ILogicalElement.LogicalParent => _parent;

    IEnumerable<ILogicalElement> ILogicalElement.LogicalChildren => Enumerable.Empty<ILogicalElement>();

    object? IPropertyInstance.Value => _value;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<LogicalTreeAttachmentEventArgs>? AttachedToLogicalTree;
    public event EventHandler<LogicalTreeAttachmentEventArgs>? DetachedFromLogicalTree;

    public virtual void FromJson(JsonNode json)
    {
        T? value = JsonSerializer.Deserialize<T>(json, JsonHelper.SerializerOptions);
        if (value != null)
        {
            Value = value;
        }
    }

    public void SetProperty()
    {
        if (Value != null)
        {
            Parent.SetValue(Property, Value);
        }
    }

    public virtual JsonNode ToJson()
    {
        return JsonSerializer.SerializeToNode(Value, JsonHelper.SerializerOptions)!;
    }

    public ISubject<T?> GetSubject()
    {
        return new SetterSubject(this);
    }

    public IObservable<T?> GetObservable()
    {
        return new SetterSubject(this);
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyname = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyname));
    }

    IObservable<Unit> IPropertyInstance.GetObservable()
    {
        return GetObservable().Select(_ => Unit.Default);
    }

    void ILogicalElement.NotifyAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        AttachedToLogicalTree?.Invoke(this, e);
    }

    void ILogicalElement.NotifyDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs e)
    {
        DetachedFromLogicalTree?.Invoke(this, e);
    }

    private sealed class SetterSubject : SubjectBase<T?>
    {
        private readonly List<IObserver<T?>> _list = new();
        private bool _isDisposed;
        private PropertyInstance<T>? _object;

        public SetterSubject(PropertyInstance<T> o)
        {
            _object = o;
            o.PropertyChanged += Object_PropertyChanged;
        }

        public override bool HasObservers => _list.Count > 0;

        [MemberNotNullWhen(false, "_object")]
        public override bool IsDisposed => _isDisposed;

        public override void Dispose()
        {
            if (!IsDisposed)
            {
                _object.PropertyChanged -= Object_PropertyChanged;
                _list.Clear();
                _object = null;
                _isDisposed = true;
            }
        }

        public override void OnCompleted()
        {
        }

        public override void OnError(Exception error)
        {
        }

        public override void OnNext(T? value)
        {
            if (_object != null)
            {
                _object.Value = value;
            }
        }

        public override IDisposable Subscribe(IObserver<T?> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));
            if (IsDisposed) throw new ObjectDisposedException(nameof(SetterSubject));

            _list.Add(observer);

            try
            {
                observer.OnNext(_object.Value);
            }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }

            return Disposable.Create((observer, _list), o =>
            {
                o.observer.OnCompleted();
                o._list.Remove(o.observer);
            });
        }

        private void Object_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PropertyInstance<T>.Value)) return;
            if (IsDisposed) throw new ObjectDisposedException(nameof(SetterSubject));

            T? value = _object.Value;
            foreach (IObserver<T?>? item in _list)
            {
                try
                {
                    item.OnNext(value);
                }
                catch (Exception ex)
                {
                    item.OnError(ex);
                }
            }
        }
    }
}
