using Beutl.Services;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class LifetimeCancellationSourceTests
{
    [Test]
    public void Cancel_ThrowingCallback_StillAllowsDispose()
    {
        var source = new LifetimeCancellationSource();
        using var registration = source.Token.Register(static () =>
            throw new InvalidOperationException("callback failed"));

        Assert.Throws<AggregateException>(source.Cancel);

        Assert.DoesNotThrow(source.Dispose);
    }
}
