using Beutl.Api.Services;
using Beutl.UnitTests.TestInfrastructure;
using NuGet.Common;

namespace Beutl.UnitTests.Api;

[TestFixture]
public class LoggerAdapterTests
{
    // NuGet shows Minimal messages even at its lowest verbosity, so they belong in the usual log.
    [Test]
    public void Log_WritesNuGetMinimalMessagesAtInformation()
    {
        using var logs = new CapturingLoggerProvider(_ => true);

        new LoggerAdapter(logs.CreateLogger("NuGet")).Log(new LogMessage(NuGet.Common.LogLevel.Minimal, "message"));

        Assert.That(
            logs.Entries.Select(entry => entry.Level),
            Is.EqualTo(new[] { Microsoft.Extensions.Logging.LogLevel.Information }));
    }
}
