using System.ComponentModel;
using System.Text.Json.Serialization;
using Beutl.JsonConverters;

namespace Beutl;

[JsonConverter(typeof(ReferenceJsonConverter))]
public interface IReference
{
    Guid Id { get; }

    CoreObject? Value { get; }

    bool IsNull { get; }

    Type ObjectType { get; }

    IReference Resolved(CoreObject obj);
}

[JsonConverter(typeof(ReferenceJsonConverter))]
public readonly struct Reference<TObject> : IEquatable<Reference<TObject>>, IReference
    where TObject : notnull, CoreObject
{
    private readonly TObject? _value;
    private readonly Guid _id;

    public Reference(Guid id) : this(id, null)
    {
    }

    public Reference(TObject value) : this(value.Id, value)
    {
    }

    public Reference(Guid id, TObject? value)
    {
        _id = id;
        _value = value;
    }

    public Reference()
    {
    }

    public Guid Id => _value?.Id ?? _id;

    public TObject? Value => _value;

    public bool IsNull => _id == Guid.Empty;

    CoreObject? IReference.Value => Value;

    Type IReference.ObjectType => typeof(TObject);

    public Reference<TObject> Resolved(TObject obj)
    {
        return new Reference<TObject>(obj);
    }

    IReference IReference.Resolved(CoreObject obj)
    {
        return Resolved((TObject)obj);
    }

    public void Deconstruct(out Guid Id, out TObject? Value)
    {
        Id = this.Id;
        Value = this.Value;
    }

    public bool Equals(Reference<TObject> other) => Id.Equals(other.Id) && EqualityComparer<TObject?>.Default.Equals(_value, other._value);

    public override bool Equals(object? obj) => obj is Reference<TObject> other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_value, _id);

    public static implicit operator Guid(Reference<TObject> reference) => reference.Id;

    public static implicit operator Reference<TObject>(Guid id) => new(id);

    public static implicit operator TObject?(Reference<TObject> reference) => reference.Value;

    public static implicit operator Reference<TObject>(TObject? value) => new(value?.Id ?? default, value);

    public static bool operator ==(Reference<TObject> left, Reference<TObject> right) => left.Equals(right);

    public static bool operator !=(Reference<TObject> left, Reference<TObject> right) => !left.Equals(right);
}
