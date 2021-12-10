using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BEditorNext.ProjectSystem;

public interface ISetter : IJsonSerializable, INotifyPropertyChanged
{
    public PropertyDefine Property { get; set; }

    public void SetProperty(Element element);
}

public class Setter<T> : ISetter
{
    private PropertyDefine<T>? _property;
    private T? _value;

    public Setter()
    {
    }

    public Setter(PropertyDefine<T> property)
    {
        Property = property;
        Value = property.GetDefaultValue();
    }

    public PropertyDefine<T> Property
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

    PropertyDefine ISetter.Property
    {
        get => Property;
        set => Property = (PropertyDefine<T>)value;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public virtual void FromJson(JsonNode json)
    {
        T? value = JsonSerializer.Deserialize<T>(json, JsonHelper.SerializerOptions);
        if (value != null)
        {
            Value = value;
        }
    }

    public void SetProperty(Element element)
    {
        if (Value != null)
        {
            element.SetValue(Property, Value);
        }
    }

    public virtual JsonNode ToJson()
    {
        return JsonValue.Create(Value)!;
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

    private sealed class SetterSubject : SubjectBase<T?>
    {
        private readonly List<IObserver<T?>> _list = new();
        private bool _isDisposed;
        private Setter<T>? _object;

        public SetterSubject(Setter<T> o)
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
            if(_object != null)
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
            if (e.PropertyName != nameof(Setter<T>.Value)) return;
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
