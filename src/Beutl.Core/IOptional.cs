namespace Beutl;

public interface IOptional
{
    bool HasValue { get; }

    Optional<object?> ToObject();

    Type GetValueType();
}
