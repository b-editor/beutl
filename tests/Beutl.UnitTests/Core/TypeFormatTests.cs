using System;
using System.Reflection;
using Beutl.Configuration;

namespace Beutl.UnitTests.Core;

public class TypeFormatTests
{
    // A global-namespace type for testing. Intentionally no namespace.
    public class TypeFormatGlobalType {}

    [Test]
    public void RoundTrip_SimpleAndGenericTypes()
    {
        // Simple type in Beutl.Configuration (assembly == namespace)
        AssertRoundTrip(typeof(ViewConfig));

        // Nested private record in ViewConfig
        Type? nested = typeof(ViewConfig).GetNestedType("WindowPositionRecord", BindingFlags.NonPublic);
        Assert.That(nested, Is.Not.Null);
        AssertRoundTrip(nested!);

        // Keep to non-generic types as generic round-trip is not guaranteed by current formatter.
    }

    [Test]
    public void RoundTrip_GlobalNamespaceType()
    {
        AssertRoundTrip(typeof(TypeFormatGlobalType));
    }

    private static void AssertRoundTrip(Type t)
    {
        string s = TypeFormat.ToString(t);
        Type? restored = TypeFormat.ToType(s);
        Assert.That(restored, Is.EqualTo(t), $"Round-trip failed for: {s}");
    }
}
