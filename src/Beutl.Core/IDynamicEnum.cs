using Beutl.Collections;

namespace Beutl;

public interface IDynamicEnum : IJsonSerializable, IEquatable<IDynamicEnum?>
{
    IDynamicEnumValue? SelectedValue { get; }

    ICoreReadOnlyList<IDynamicEnumValue> Values { get; }

    IDynamicEnum WithNewValue(IDynamicEnumValue? value);

    bool IEquatable<IDynamicEnum?>.Equals(IDynamicEnum? other)
    {
        return (SelectedValue?.Equals(other?.SelectedValue) == true
            || (other?.SelectedValue == null && SelectedValue == null))
            && other?.GetType() == GetType();
    }
}

public interface IDynamicEnumValue : IEquatable<IDynamicEnumValue?>
{
    string DisplayName { get; }
}
