using System.Collections;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl;

public interface ICoreObject : INotifyPropertyChanged, INotifyDataErrorInfo, ICoreSerializable
{
    Guid Id { get; set; }

    string Name { get; set; }

    void BeginBatchUpdate();

    bool EndBatchUpdate();

    void ClearValue<TValue>(CoreProperty<TValue> property);

    void ClearValue(CoreProperty property);

    TValue GetValue<TValue>(CoreProperty<TValue> property);

    object? GetValue(CoreProperty property);

    void SetValue<TValue>(CoreProperty<TValue> property, TValue? value);

    void SetValue(CoreProperty property, object? value);
}

public abstract class CoreObject : ICoreObject
{
    public static readonly CoreProperty<Guid> IdProperty;

    public static readonly CoreProperty<string> NameProperty;
    private Dictionary<int, IEntry>? _values;
    private Dictionary<int, IBatchEntry>? _batchChanges;
    private Dictionary<int, string>? _errors;
    private bool _batchApplying;

    private int _batchUpdateCount;

    internal interface IEntry
    {
    }

    internal interface IBatchEntry
    {
        void ApplyTo(CoreObject obj, CoreProperty property);
    }

    internal sealed class Entry<T> : IEntry
    {
        public T? Value;
    }

    internal sealed class BatchEntry<T> : IBatchEntry
    {
        public T? OldValue;
        public T? NewValue;

        public void ApplyTo(CoreObject obj, CoreProperty property)
        {
            obj.SetValue(property, NewValue);
        }
    }

    static CoreObject()
    {
        IdProperty = ConfigureProperty<Guid, CoreObject>(nameof(Id))
            .DefaultValue(Guid.Empty)
            .Register();

        NameProperty = ConfigureProperty<string, CoreObject>(nameof(Name))
            .DefaultValue(string.Empty)
            .Register();
    }

    protected CoreObject()
    {
        Id = Guid.NewGuid();
    }

    [Browsable(false)]
    public Guid Id
    {
        get => GetValue(IdProperty);
        set => SetValue(IdProperty, value);
    }

    [Browsable(false)]
    public string Name
    {
        get => GetValue(NameProperty);
        set => SetValue(NameProperty, value);
    }

    public bool BatchUpdate => _batchUpdateCount > 0;

    private Dictionary<int, IEntry> Values => _values ??= [];

    private Dictionary<int, IBatchEntry> BatchChanges => _batchChanges ??= [];

    private Dictionary<int, string> Errors => _errors ??= [];

    public bool HasErrors => _errors?.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public static CorePropertyBuilder<T, TOwner> ConfigureProperty<T, TOwner>(string name)
    {
        return new CorePropertyBuilder<T, TOwner>(name);
    }

    public static CorePropertyBuilder<T, TOwner> ConfigureProperty<T, TOwner>(Expression<Func<TOwner, T>> exp)
    {
        return new CorePropertyBuilder<T, TOwner>(exp);
    }

    public IEnumerable GetErrors(string? propertyName)
    {
        if (_errors == null)
        {
            return Enumerable.Empty<string>();
        }

        if (string.IsNullOrEmpty(propertyName))
        {
            return _errors.Values;
        }
        else
        {
            CoreProperty? property = PropertyRegistry.FindRegistered(this, propertyName);
            if (property != null && _errors.TryGetValue(property.Id, out string? message))
            {
                return Enumerable.Repeat(message, 1);
            }
            else
            {
                return Enumerable.Empty<string>();
            }
        }
    }

    public void BeginBatchUpdate()
    {
        _batchUpdateCount++;
    }

    public bool EndBatchUpdate()
    {
        _batchUpdateCount--;
        if (_batchUpdateCount < 0)
        {
            return false;
        }

        if (_batchUpdateCount == 0)
        {
            if (_batchChanges != null)
            {
                try
                {
                    _batchApplying = true;
                    foreach (KeyValuePair<int, IBatchEntry> item in _batchChanges)
                    {
                        if (PropertyRegistry.FindRegistered(item.Key) is CoreProperty property)
                        {
                            item.Value.ApplyTo(this, property);
                        }
                    }
                    _batchChanges.Clear();
                }
                finally
                {
                    _batchApplying = false;
                }
            }

            return true;
        }
        else
        {
            return false;
        }
    }

    public TValue GetValue<TValue>(CoreProperty<TValue> property)
    {
        Type ownerType = GetType();
        if (!ownerType.IsAssignableTo(property.OwnerType))
        {
            throw new InvalidOperationException("Owner does not match.");
        }

        if (property is StaticProperty<TValue> staticProperty)
        {
            return staticProperty.RouteGetTypedValue(this)!;
        }

        if (BatchUpdate)
        {
            if (_batchChanges?.TryGetValue(property.Id, out IBatchEntry? entry) == true &&
               entry is BatchEntry<TValue> entryT)
            {
                return entryT.NewValue!;
            }
            else
            {
                goto ReturnDefault;
            }
        }
        else if (_values?.TryGetValue(property.Id, out IEntry? entry) == true &&
            entry is Entry<TValue> entryT)
        {
            return entryT.Value!;
        }

    ReturnDefault:
        return property.GetMetadata<CorePropertyMetadata<TValue>>(ownerType).DefaultValue!;
    }

    public object? GetValue(CoreProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property.RouteGetValue(this);
    }

    private void ValidateProperty<TValue>(
        CorePropertyMetadata<TValue> metadata, CoreProperty<TValue> property, ref TValue? value)
    {
        if (metadata.Validator is IValidator<TValue> validator)
        {
            bool oldHasErrors = HasErrors;
            string? oldError = _errors != null && _errors.TryGetValue(property.Id, out string? v) ? v : null;
            string? newError;

            var vcontext = new ValidationContext(this, property);
            if (!validator.TryCoerce(vcontext, ref value)
                && validator.Validate(vcontext, value) is string message)
            {
                // 値の強制が失敗、検証に失敗した場合、エラーメッセージを設定
                Errors[property.Id] = message;
                newError = message;
            }
            else
            {
                Errors.Remove(property.Id);
                newError = null;
            }

            if (ErrorsChanged != null && oldError != newError)
            {
                ErrorsChanged.Invoke(this, new DataErrorsChangedEventArgs(property.Name));
            }

            if (oldHasErrors != HasErrors)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
            }
        }
    }

    public void SetValue<TValue>(CoreProperty<TValue> property, TValue? value)
    {
        if (value != null && !value.GetType().IsAssignableTo(property.PropertyType))
        {
            throw new InvalidOperationException($"{nameof(value)} of type {value.GetType().Name} cannot be assigned to type {property.PropertyType}.");
        }

        Type ownerType = GetType();
        if (!ownerType.IsAssignableTo(property.OwnerType))
        {
            throw new InvalidOperationException("Owner does not match.");
        }

        if (property is StaticProperty<TValue> staticProperty)
        {
            staticProperty.RouteSetTypedValue(this, value);
            return;
        }

        CorePropertyMetadata<TValue>? metadata = property.GetMetadata<CorePropertyMetadata<TValue>>(ownerType);
        ValidateProperty(metadata, property, ref value);

        if (BatchUpdate)
        {
            if (_batchChanges != null &&
                _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry) &&
                oldEntry is BatchEntry<TValue> entryT)
            {
                entryT.NewValue = value;
            }
            else
            {
                entryT = new BatchEntry<TValue>
                {
                    NewValue = value,
                };
                BatchChanges[property.Id] = entryT;
            }
        }
        else
        {
            if (_values != null &&
                _values.TryGetValue(property.Id, out IEntry? oldEntry) &&
                oldEntry is Entry<TValue> entryT)
            {
                TValue? oldValue = entryT.Value;
                if (!EqualityComparer<TValue>.Default.Equals(oldValue, value))
                {
                    entryT.Value = value;
                    RaisePropertyChanged(property, metadata, value, oldValue);
                }
            }
            else
            {
                if (!EqualityComparer<TValue>.Default.Equals(metadata.DefaultValue, value))
                {
                    entryT = new Entry<TValue>
                    {
                        Value = value,
                    };
                    Values[property.Id] = entryT;
                    RaisePropertyChanged(property, metadata, value, metadata.DefaultValue);
                }
            }
        }
    }

    public void SetValue(CoreProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.RouteSetValue(this, value!);
    }

    public void ClearValue<TValue>(CoreProperty<TValue> property)
    {
        CorePropertyMetadata<TValue> metadata = property.GetMetadata<CorePropertyMetadata<TValue>>(GetType());
        if (metadata.HasDefaultValue)
        {
            SetValue(property, metadata.DefaultValue);
        }
    }

    public void ClearValue(CoreProperty property)
    {
        SetValue(property, property.GetMetadata<CorePropertyMetadata>(GetType()).GetDefaultValue());
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        if (args is CorePropertyChangedEventArgs coreArgs)
        {
            if (coreArgs.Property.HasObservers)
            {
                coreArgs.Property.NotifyChanged(coreArgs);
            }

            if (coreArgs.PropertyMetadata.Notifiable)
            {
                PropertyChanged?.Invoke(this, args);
            }
        }
        else
        {
            PropertyChanged?.Invoke(this, args);
        }
    }

    protected bool SetAndRaise<T>(CoreProperty<T> property, ref T field, T value)
    {
        CorePropertyMetadata<T>? metadata = property.GetMetadata<CorePropertyMetadata<T>>(GetType());
        ValidateProperty(metadata, property, ref value!);

        bool result = !EqualityComparer<T>.Default.Equals(field, value);
        if (BatchUpdate)
        {
            if (_batchChanges != null &&
                _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry) &&
                oldEntry is BatchEntry<T> entryT)
            {
                entryT.NewValue = value;
            }
            else
            {
                entryT = new BatchEntry<T>
                {
                    OldValue = field,
                    NewValue = value,
                };
                BatchChanges[property.Id] = entryT;
            }

            field = value;
        }
        else if (_batchApplying
            && _batchChanges != null
            && _batchChanges.TryGetValue(property.Id, out IBatchEntry? oldEntry)
            && oldEntry is BatchEntry<T> entryT)
        {
            // バッチ適用中
            field = value;

            if (!EqualityComparer<T>.Default.Equals(entryT.OldValue, value))
            {
                RaisePropertyChanged(property, metadata!, value, entryT.OldValue);
            }
        }
        else if (result)
        {
            T old = field;
            field = value;

            RaisePropertyChanged(property, metadata, value, old);
        }

        return result;
    }

    private void RaisePropertyChanged<T>(CoreProperty<T> property, CorePropertyMetadata metadata, T? newValue, T? oldValue)
    {
        var eventArgs = new CorePropertyChangedEventArgs<T>(this, property, metadata, newValue, oldValue);

        OnPropertyChanged(eventArgs);
    }

    public virtual void Serialize(ICoreSerializationContext context)
    {
        Type ownerType = GetType();

        IReadOnlyList<CoreProperty> list = PropertyRegistry.GetRegistered(ownerType);
        for (int i = 0; i < list.Count; i++)
        {
            CoreProperty item = list[i];
            item.RouteSerialize(context, GetValue(item));
        }
    }

    public virtual void Deserialize(ICoreSerializationContext context)
    {
        Type ownerType = GetType();

        IReadOnlyList<CoreProperty> list = PropertyRegistry.GetRegistered(ownerType);
        for (int i = 0; i < list.Count; i++)
        {
            CoreProperty item = list[i];
            Optional<object?> value = item.RouteDeserialize(context);
            if (value.HasValue)
            {
                SetValue(item, value.Value);
            }
        }
    }
}
