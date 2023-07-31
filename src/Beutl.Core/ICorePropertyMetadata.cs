using System.ComponentModel.DataAnnotations;

using Beutl.Validation;

namespace Beutl;

public interface ICorePropertyMetadata
{
    Type PropertyType { get; }

    DisplayAttribute? DisplayAttribute { get; }

    Attribute[]? Attributes { get; }

    void Merge(ICorePropertyMetadata baseMetadata, CoreProperty? property);

    object? GetDefaultValue();

    IValidator? GetValidator();
}
