using Beutl.Api.Services;
using Beutl.Extensibility;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class ExtensionProviderTests
{
    private sealed class StubExtension : Extension;

    private sealed class OtherStubExtension : Extension;

    [Test]
    public void ExtensionProvider_ImplementsIExtensionProvider()
    {
        var provider = new ExtensionProvider();

        Assert.That(provider, Is.InstanceOf<IExtensionProvider>());
    }

    [Test]
    public void IExtensionProvider_AllExtensions_ReflectsAddedExtensions()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;

        Assert.That(abstraction.AllExtensions, Is.Empty);

        var ext = new StubExtension();
        provider.AddExtensions(1, [ext]);

        Assert.That(abstraction.AllExtensions, Has.Member(ext));
    }

    [Test]
    public void IExtensionProvider_GetExtensions_FiltersByType()
    {
        var provider = new ExtensionProvider();
        IExtensionProvider abstraction = provider;

        var first = new StubExtension();
        var second = new OtherStubExtension();
        provider.AddExtensions(1, [first]);
        provider.AddExtensions(2, [second]);

        StubExtension[] matched = abstraction.GetExtensions<StubExtension>();

        Assert.That(matched, Is.EquivalentTo(new[] { first }));
    }

    [Test]
    public void IExtensionProvider_MatchEditorExtension_ReturnsNullForUnknownFile()
    {
        IExtensionProvider abstraction = new ExtensionProvider();

        Assert.That(abstraction.MatchEditorExtension("unknown.bogus"), Is.Null);
    }
}
