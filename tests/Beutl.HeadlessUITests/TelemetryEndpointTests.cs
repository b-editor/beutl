using Beutl.Services;

namespace Beutl.HeadlessUITests;

// BEUTL_TELEMETRY_ENDPOINT sends traces and logs to another OTLP/HTTP collector. Anything that is not an absolute
// http or https URL without a query or fragment leaves them going to the default collector.
[TestFixture]
public sealed class TelemetryEndpointTests
{
    [TestCase("http://localhost:4318", ExpectedResult = "http://localhost:4318")]
    [TestCase(" http://localhost:4318/ ", ExpectedResult = "http://localhost:4318")]
    [TestCase("https://collector.example/otel/", ExpectedResult = "https://collector.example/otel")]
    public string A_collector_url_replaces_the_default(string configured)
        => Telemetry.ResolveBaseUrl(configured);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("localhost:4318")]
    [TestCase("file:///tmp/otel")]
    [TestCase("not a url")]
    [TestCase("https://collector.example/otel?token=abc")]
    [TestCase("https://collector.example/otel#part")]
    public void Anything_else_keeps_the_default_collector(string? configured)
    {
        Assert.That(Telemetry.ResolveBaseUrl(configured), Is.EqualTo("https://otel.beditor.net"));
    }
}
