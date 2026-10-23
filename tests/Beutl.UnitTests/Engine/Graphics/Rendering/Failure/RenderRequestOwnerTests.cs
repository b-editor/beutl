using Beutl.Graphics.Rendering.Requests;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Failure;

[TestFixture]
public sealed class RenderRequestOwnerTests
{
    [Test]
    public void ReportMaskedFailures_IgnoresLoggingProviderFailures()
    {
        using RenderRequestOwnerLogCapture logs = RenderRequestOwnerLogCapture.Start(throwOnLog: true);
        var primary = new ApplicationException("render-primary");
        var first = new InvalidOperationException("render-secondary-1");
        var second = new InvalidOperationException("render-secondary-2");
        using var owner = new RenderRequestOwner();

        owner.RecordPrimaryFailure(primary);
        owner.RecordPrimaryFailure(first);
        owner.RecordPrimaryFailure(second);

        Assert.That(() => owner.ReportMaskedFailures(primary), Throws.Nothing);
        Assert.That(
            logs.Entries.Select(entry => entry.Exception),
            Is.EqualTo(new Exception[] { first, second }),
            "a failing provider must not stop the remaining failures from being reported");
    }
}
