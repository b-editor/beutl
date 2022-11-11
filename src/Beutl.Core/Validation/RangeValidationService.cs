using System.Numerics;

namespace Beutl.Validation;

public sealed class RangeValidationService
{
    public static readonly RangeValidationService Instance = new();
    private readonly Dictionary<Type, Type> _services = new()
    {
        { typeof(Vector2), typeof(Vector2RangeValidator) },
        { typeof(Vector3), typeof(Vector3RangeValidator) },
        { typeof(Vector4), typeof(Vector4RangeValidator) },
    };

    public RangeValidationService()
    {
        RegisterNumber<byte>();
        RegisterNumber<decimal>();
        RegisterNumber<double>();
        RegisterNumber<float>();
        RegisterNumber<short>();
        RegisterNumber<int>();
        RegisterNumber<long>();
        RegisterNumber<sbyte>();
        RegisterNumber<ushort>();
        RegisterNumber<uint>();
        RegisterNumber<ulong>();
    }

    public void RegisterNumber<TNumber>()
        where TNumber : struct, INumber<TNumber>, IMinMaxValue<TNumber>
    {
        _services.Add(typeof(TNumber), typeof(NumberRangeValidator<TNumber>));
    }

    public void Register<T, TValidator>()
        where TValidator : RangeValidator<T>
    {
        _services.Add(typeof(T), typeof(TValidator));
    }

    public void Register(Type type, Type validatorType)
    {
        if (typeof(RangeValidator<>).MakeGenericType(type).IsAssignableFrom(validatorType) && !validatorType.IsAbstract)
        {
            _services.Add(type, validatorType);
        }
        else
        {
            throw new InvalidOperationException();
        }
    }

    public Type Get<T>()
    {
        return Get(typeof(T));
    }

    public Type Get(Type type)
    {
        if (_services.TryGetValue(type, out Type? value))
        {
            return value;
        }
        else if (Attribute.GetCustomAttribute(type, typeof(RangeValidatableAttribute)) is RangeValidatableAttribute att)
        {
            _services.Add(type, att.ValidatorType);
            return att.ValidatorType;
        }
        else
        {
            throw new Exception();
        }
    }
}
