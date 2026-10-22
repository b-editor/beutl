using System.Collections;
using System.ComponentModel;
using System.Text.Json.Nodes;
using Beutl.Serialization;

namespace Beutl;

public interface ICoreObject : INotifyPropertyChanged, INotifyDataErrorInfo, ICoreSerializable
{
    Guid Id { get; set; }

    string Name { get; set; }
}

public abstract partial class CoreObject : ICoreObject
{
    // Validation errors by property name.
    private Dictionary<string, string>? _errors;

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

    private Dictionary<string, string> Errors => _errors ??= [];

    public bool HasErrors => _errors?.Count > 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

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
        else if (_errors.TryGetValue(propertyName, out string? message))
        {
            return Enumerable.Repeat(message, 1);
        }
        else
        {
            return Enumerable.Empty<string>();
        }
    }

    // Records the validation error of the property named name, or clears it when message is null.
    internal void SetPropertyError(string name, string? message)
    {
        if (message == null && _errors == null) return;

        bool oldHasErrors = HasErrors;
        string? oldError = _errors != null && _errors.TryGetValue(name, out string? v) ? v : null;
        if (message != null)
        {
            Errors[name] = message;
        }
        else
        {
            _errors!.Remove(name);
        }

        if (ErrorsChanged != null && oldError != message)
        {
            ErrorsChanged.Invoke(this, new DataErrorsChangedEventArgs(name));
        }

        if (oldHasErrors != HasErrors)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasErrors)));
        }
    }

    // Called after a value-only property this object registered changed its value.
    internal virtual void OnPropertyValueChanged(IProperty property)
    {
    }

    protected virtual void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        PropertyChanged?.Invoke(this, args);
    }

    public virtual void Serialize(ICoreSerializationContext context)
    {
        IProperty[] saved = [.. RegisteredProperties.Where(IsAutoSerialized)];
        // Base fields come first, then Id and Name, as files have them.
        foreach (IProperty property in saved)
        {
            if (IsBaseField(property) && !IsOwnField(property))
                property.SerializeValue(context);
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
        foreach (IProperty property in RegisteredProperties)
        {
            // A list its owner keeps is read by the owner into that list.
            if (IsAutoSerialized(property) && !(IsBaseField(property) && property is IValueOnlyProperty { KeepsList: true }))
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
