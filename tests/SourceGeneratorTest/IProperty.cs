using System;
using System.Collections.Generic;

using Beutl.Validation;

namespace Beutl;

public interface IProperty
{
    void SetAttributes(string name, Attribute[] attributes);

    void SetOwnerObject(CoreObject? owner);

    IValidator CreateValidator(Attribute[] attributes);

    void SetValidator(IValidator validator);
}

public interface IProperty<T> : IProperty
{
}

public interface IExpressionProperty<T> : IProperty<T>
{
}

public class CoreObject
{
    public virtual IReadOnlyList<IProperty> Properties => throw null!;

    protected virtual IEnumerable<IProperty> ScanPropertiesCore<T>() where T : CoreObject
    {
        throw null!;
    }
}
