using Beutl.Engine;

namespace SourceGeneratorTest;
public partial class Derived : EngineObject
{
    public IProperty<float> X { get; } = Property.Create(0f);

    public IProperty<float> Y { get; } = Property.Create(0f);
}

public partial class Derived2 : Derived
{
    public IProperty<float> Z { get; } = Property.Create(0f);
}

public partial class Derived3 : Derived
{
    // Listっぽいもの、Listで終わる型に対して
    public List<Derived> Children { get; } = [];

    // EngineObjectの派生型に対して
    public IProperty<Derived> Child { get; } = Property.Create<Derived>(null!);
}
