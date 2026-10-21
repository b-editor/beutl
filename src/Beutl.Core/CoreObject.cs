using System.Collections;
using System.ComponentModel;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using Beutl.Collections;
using Beutl.Serialization;
using Beutl.Validation;

namespace Beutl;

public interface ICoreObject : INotifyPropertyChanged, INotifyDataErrorInfo, ICoreSerializable
{
    Guid Id { get; set; }

    string Name { get; set; }

    void ClearValue<TValue>(CoreProperty<TValue> property);

    void ClearValue(CoreProperty property);

    TValue GetValue<TValue>(CoreProperty<TValue> property);

    object? GetValue(CoreProperty property);

    void SetValue<TValue>(CoreProperty<TValue> property, TValue? value);

    void SetValue(CoreProperty property, object? value);
}

public abstract partial class CoreObject : ICoreObject
{
    private Dictionary<int, IEntry>? _values;
    private Dictionary<int, string>? _errors;
    private CoreProperty? _forcedReplacementProperty;

    internal interface IEntry
    {
    }

    internal sealed class Entry<T> : IEntry
    {
        public T? Value;
    }

    // Shared by every object, so that none allocates handlers of its own.
    private static readonly EventHandler<PropertyValueChangedEventArgs<Guid>> s_idChanged =
        static (_, e) => e.Property.GetOwnerObject()?.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Id)));

    private static readonly EventHandler<PropertyValueChangedEventArgs<string>> s_nameChanged =
        static (_, e) => e.Property.GetOwnerObject()?.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));

    protected CoreObject()
    {
        RegisterOwnField(IdProperty, nameof(Id));
        RegisterOwnField(NameProperty, nameof(Name));
        IdProperty.ValueChanged += s_idChanged;
        NameProperty.ValueChanged += s_nameChanged;
        Id = Guid.NewGuid();
    }

    // Every object has these, so the constructor registers them without a scan. They raise no Edited.
    public IProperty<Guid> IdProperty { get; } = new ValueOnlyProperty<Guid>(Guid.Empty) { AffectsRender = false };

    public IProperty<string> NameProperty { get; } = new ValueOnlyProperty<string>(string.Empty) { AffectsRender = false };

    [Browsable(false)]
    public Guid Id
    {
        get => IdProperty.Value;
        set => IdProperty.Value = value;
    }

    [Browsable(false)]
    public string Name
    {
        get => NameProperty.Value;
        set => NameProperty.Value = value;
    }

    public Uri? Uri { get; set; }

    // Non-null while this object stands in for a file the serializer must not regenerate:
    // StoreToUri skips the source location and copies the raw text verbatim to any new one.
    internal SuppressedStorageSource? SuppressedStorageSource
    {
        get;
        set
        {
            bool wasSuppressed = field is not null;
            field = value;
            if (wasSuppressed != value is not null)
            {
                OnStorageSuppressionChanged();
            }
        }
    }

    // Called after SuppressedStorageSource changes between null and non-null.
    internal virtual void OnStorageSuppressionChanged()
    {
    }

    // The members of the JSON this object was read from that it did not read, usually ones that a newer
    // version of Beutl or of an extension wrote. Serializing the object writes them back after its own
    // members, so an older version keeps that data instead of dropping it on save.
    internal JsonObject? UnknownMembers { get; private set; }

    // Adds members to UnknownMembers; a member read again replaces the one kept before.
    internal void KeepUnknownMembers(JsonObject members)
    {
        if (UnknownMembers is null)
        {
            UnknownMembers = members;
            return;
        }

        foreach ((string key, JsonNode? node) in members)
        {
            UnknownMembers[key] = node?.DeepClone();
        }
    }

    private Dictionary<int, IEntry> Values => _values ??= [];

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

        if (_values?.TryGetValue(property.Id, out IEntry? entry) == true &&
                 entry is Entry<TValue> entryT)
        {
            return entryT.Value!;
        }

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
        SetValueCore(property, value, forceReferenceReplacement: false);
    }

    internal void ReplaceValue<TValue>(CoreProperty<TValue> property, TValue? value)
    {
        SetValueCore(property, value, forceReferenceReplacement: true);
    }

    internal void ReplaceValue(CoreProperty property, object? value)
    {
        ArgumentNullException.ThrowIfNull(property);

        property.RouteReplaceValue(this, value);
    }

    private void SetValueCore<TValue>(
        CoreProperty<TValue> property,
        TValue? value,
        bool forceReferenceReplacement)
    {
        if (value != null && !value.GetType().IsAssignableTo(property.PropertyType))
        {
            throw new InvalidOperationException(
                $"{nameof(value)} of type {value.GetType().Name} cannot be assigned to type {property.PropertyType}.");
        }

        Type ownerType = GetType();
        if (!ownerType.IsAssignableTo(property.OwnerType))
        {
            throw new InvalidOperationException("Owner does not match.");
        }

        if (property is StaticProperty<TValue> staticProperty)
        {
            CoreProperty? previous = _forcedReplacementProperty;
            _forcedReplacementProperty = forceReferenceReplacement ? property : null;
            try
            {
                staticProperty.RouteSetTypedValue(this, value);
            }
            finally
            {
                _forcedReplacementProperty = previous;
            }
            return;
        }

        DocumentWriteCheck.Verify(this, property.Name);
        CorePropertyMetadata<TValue>? metadata = property.GetMetadata<CorePropertyMetadata<TValue>>(ownerType);
        ValidateProperty(metadata, property, ref value);

        if (_values != null &&
            _values.TryGetValue(property.Id, out IEntry? oldEntry) &&
            oldEntry is Entry<TValue> entryT)
        {
            TValue? oldValue = entryT.Value;
            if (ValueReplacement.RequiresReplacement(oldValue, value, forceReferenceReplacement))
            {
                entryT.Value = value;
                RaisePropertyChanged(property, metadata, value, oldValue);
            }
        }
        else
        {
            if (ValueReplacement.RequiresReplacement(metadata.DefaultValue, value, forceReferenceReplacement))
            {
                entryT = new Entry<TValue> { Value = value, };
                Values[property.Id] = entryT;
                RaisePropertyChanged(property, metadata, value, metadata.DefaultValue);
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
        return SetAndRaise(property, ref field, value, forceReferenceReplacement: false);
    }

    protected bool SetAndRaise<T>(
        CoreProperty<T> property,
        ref T field,
        T value,
        bool forceReferenceReplacement)
    {
        DocumentWriteCheck.Verify(this, property.Name);
        if (ReferenceEquals(_forcedReplacementProperty, property))
        {
            forceReferenceReplacement = true;
            _forcedReplacementProperty = null;
        }

        CorePropertyMetadata<T>? metadata = property.GetMetadata<CorePropertyMetadata<T>>(GetType());
        ValidateProperty(metadata, property, ref value!);

        bool result = ValueReplacement.RequiresReplacement(field, value, forceReferenceReplacement);
        if (result)
        {
            T old = field;
            field = value;

            RaisePropertyChanged(property, metadata, value, old);
        }

        return result;
    }

    private void RaisePropertyChanged<T>(CoreProperty<T> property, CorePropertyMetadata metadata, T? newValue,
        T? oldValue)
    {
        var eventArgs = new CorePropertyChangedEventArgs<T>(this, property, metadata, newValue, oldValue);

        OnPropertyChanged(eventArgs);
    }

    public virtual void Serialize(ICoreSerializationContext context)
    {
        IProperty[] saved = [.. RegisteredProperties.Where(IsAutoSerialized)];
        // Base fields come first, where the CoreProperty fields they replace were written.
        foreach (IProperty property in saved)
        {
            if (IsBaseField(property) && !IsOwnField(property))
                property.SerializeValue(context);
        }

        Type ownerType = GetType();

        IReadOnlyList<CoreProperty> list = PropertyRegistry.GetRegistered(ownerType);
        for (int i = 0; i < list.Count; i++)
        {
            CoreProperty item = list[i];
            item.RouteSerialize(context, GetValue(item));
        }

        IdProperty.SerializeValue(context);
        NameProperty.SerializeValue(context);
        SerializeMembers(context, saved);
        foreach (IProperty property in saved)
        {
            if (!IsBaseField(property) && !IsOwnField(property))
                property.SerializeValue(context);
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
                if (value.Value is IReference { IsNull: false } reference)
                {
                    context.Resolve(reference.Id,
                        resolved => SetValue(item, reference.Resolved((CoreObject)resolved)));
                }

                SetValue(item, value.Value);
            }
        }

        foreach (IProperty property in RegisteredProperties)
        {
            // A base field that holds a list keeps that list; its owner reads the items into it.
            if (IsAutoSerialized(property) && !(IsBaseField(property) && property.Value is ICoreList))
                property.DeserializeValue(context);
        }

        DeserializeMembers(context);
    }

    // What a kind of object saves besides property values, such as EngineObject's animations and expressions.
    internal virtual void SerializeMembers(ICoreSerializationContext context, IProperty[] saved)
    {
    }

    internal virtual void DeserializeMembers(ICoreSerializationContext context)
    {
    }
}
