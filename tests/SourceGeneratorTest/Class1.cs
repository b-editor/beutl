using Beutl.Engine;

namespace SourceGeneratorTest;

public enum TextAlignment
{
    Start,
    Center,
    End,
}

public partial class Derived : EngineObject
{
    public IProperty<float> X { get; } = Property.Create(0f);

    public IProperty<float> Y { get; } = Property.Create(17f);

    public IProperty<TextAlignment> TextAlignment { get; } = Property.Create(SourceGeneratorTest.TextAlignment.Start);
}

public partial class Derived2 : Derived
{
    public IProperty<float> Z { get; } = Property.Create(0f);
}

public partial class Derived3 : Derived
{
    // EngineObjectの派生型に対して
    public IProperty<Derived> Child { get; } = Property.Create(new Derived());

    public IProperty<Derived?> OptionalChild { get; } = Property.Create<Derived?>(null);

    // IListProperty<T> over an EngineObject element exercises the generated list path
    // (ResourceReconciler.ReconcileChildren + per-item disposal).
    public IListProperty<Derived> Items { get; } = null!;
}
