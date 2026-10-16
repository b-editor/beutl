using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

public sealed partial class PackageApiCheckerTests
{
    private const string InterfacesV1 = """
        namespace HostLib;
        public interface IHandler { void Handle(int value); }
        public interface IConverter<T> { T Convert(string text); }
        public interface INamed { string Name { get; } }
        public abstract class Base : INamed { public string Name => "base"; }
        """;

    // Adds the abstract IHandler.Reset, and IConverter<T>.CanConvert with a default implementation.
    private const string InterfacesV2 = """
        namespace HostLib;
        public interface IHandler { void Handle(int value); void Reset(); }
        public interface IConverter<T> { T Convert(string text); bool CanConvert(string text) => true; }
        public interface INamed { string Name { get; } }
        public abstract class Base : INamed { public string Name => "base"; }
        """;

    [Test]
    public void Reports_the_abstract_interface_methods_that_a_package_type_does_not_implement()
    {
        string v1 = Compile("v1", "HostLib", "1.0.0.0", InterfacesV1);
        string plugin = Compile("plugin", "Plugin", "1.0.0.0", """
            using HostLib;
            public sealed class Handler : IHandler { public void Handle(int value) { } }
            internal sealed class Explicit : IHandler { void IHandler.Handle(int value) { } }
            public sealed class Converter : IConverter<double> { public double Convert(string text) => 0; }
            public sealed class Named : Base, INamed;
            """, v1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(PackageApiChecker.Check([plugin], Host(v1)), Is.Empty);
            Assert.That(
                PackageApiChecker.Check([plugin], Host(Compile("v2", "HostLib", "2.0.0.0", InterfacesV2))),
                Is.EqualTo(new PackageApiProblem[] { new(PackageApiProblemKind.UnimplementedMethod, "HostLib", "HostLib.IHandler.Reset()") }));
        }
    }
}
