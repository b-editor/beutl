using System;

using Beutl.Validation;

namespace Beutl.Engine;

public interface IProperty
{
    void SetAttributes(string name, Attribute[] attributes);

    void SetOwnerObject(EngineObject? owner);

    IValidator CreateValidator(Attribute[] attributes);

    void SetValidator(IValidator validator);
}

public interface IProperty<T> : IProperty
{
}
