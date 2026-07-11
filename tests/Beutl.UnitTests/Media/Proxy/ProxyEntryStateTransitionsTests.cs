using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyEntryStateTransitionsTests
{
    [TestCase(ProxyState.None, ProxyState.Generating)]
    [TestCase(ProxyState.Generating, ProxyState.Ready)]
    [TestCase(ProxyState.Generating, ProxyState.Failed)]
    [TestCase(ProxyState.Generating, ProxyState.None)]
    [TestCase(ProxyState.Generating, ProxyState.Partial)]
    [TestCase(ProxyState.Ready, ProxyState.Stale)]
    [TestCase(ProxyState.Ready, ProxyState.None)]
    [TestCase(ProxyState.Stale, ProxyState.Generating)]
    [TestCase(ProxyState.Failed, ProxyState.Generating)]
    [TestCase(ProxyState.Partial, ProxyState.None)]
    public void IsLegal_ReturnsTrue_ForDocumentedTransitions(ProxyState from, ProxyState to)
    {
        Assert.That(ProxyStateTransitions.IsLegal(from, to), Is.True);
    }

    [TestCase(ProxyState.None, ProxyState.Ready)]
    [TestCase(ProxyState.Ready, ProxyState.Generating)]
    [TestCase(ProxyState.Stale, ProxyState.Ready)]
    [TestCase(ProxyState.Failed, ProxyState.Ready)]
    [TestCase(ProxyState.Partial, ProxyState.Ready)]
    public void IsLegal_ReturnsFalse_ForIllegalTransitions(ProxyState from, ProxyState to)
    {
        Assert.That(ProxyStateTransitions.IsLegal(from, to), Is.False);
    }
}
