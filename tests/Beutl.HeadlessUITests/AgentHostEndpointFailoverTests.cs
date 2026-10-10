using System.Net;
using System.Threading.Channels;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentHostEndpointFailoverTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Verified_shared_url_survives_transient_probes_but_expires_and_rejects_foreign_proofs(bool timeout)
    {
        string directory = Path.Combine(Path.GetTempPath(), "beutl-failover-tests-" + Guid.NewGuid().ToString("N"));
        var registry = new AgentHostInstanceRegistry(directory);
        string ownerId = Guid.NewGuid().ToString("N");
        string survivorId = Guid.NewGuid().ToString("N");
        var owner = new AgentHostInstanceAuthentication("instance-test-token", ownerId);
        var foreign = new AgentHostInstanceAuthentication("other-profile-token", ownerId);
        int probeMode = 0;
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using WebApplication listener = builder.Build();
        listener.MapGet("/agent-host/identity", async (HttpContext context) =>
        {
            if (Volatile.Read(ref probeMode) == 1)
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            return Results.Json((Volatile.Read(ref probeMode) == 2 ? foreign : owner)
                .CreateProof(context.Request.Query["challenge"].ToString()));
        });
        IDisposable? ownerRegistration = null;
        try
        {
            await listener.StartAsync();
            var sharedUri = new Uri(new Uri(listener.Urls.Single()), "/mcp");
            var directUri = new Uri("http://127.0.0.1:1/mcp");
            ownerRegistration = registry.Register(ownerId, sharedUri);
            using IDisposable survivorRegistration = registry.Register(survivorId, directUri);
            var clock = new ManualTimeProvider();
            using var failover = new AgentHostEndpointFailover(registry, "instance-test-token", survivorId, sharedUri.Port, clock);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            if (timeout)
            {
                Volatile.Write(ref probeMode, 1);
                // A timeout cannot establish initial trust in the shared listener.
                Assert.That(await failover.ResolveConnectionUriAsync(directUri, cancellation.Token), Is.EqualTo(directUri));
                Volatile.Write(ref probeMode, 0);
            }
            Assert.That(await failover.ResolveConnectionUriAsync(directUri, cancellation.Token), Is.EqualTo(sharedUri));
            if (timeout)
                Volatile.Write(ref probeMode, 1);
            else
            {
                // Match StopAsync: discovery disappears while Kestrel still holds the port.
                ownerRegistration.Dispose();
                ownerRegistration = null;
            }
            clock.Advance(TimeSpan.FromSeconds(3));
            var addresses = new ServerAddressesFeature();
            addresses.Addresses.Add(directUri.GetLeftPart(UriPartial.Authority));
            Channel<Uri> publications = Channel.CreateUnbounded<Uri>();
            Task monitor = failover.RunAsync(addresses, directUri, uri => publications.Writer.TryWrite(uri), cancellation.Token);
            try
            {
                Assert.That(await publications.Reader.ReadAsync(cancellation.Token), Is.EqualTo(sharedUri),
                    "A transient probe must not offer a direct URL for settings installation.");
                clock.Advance(TimeSpan.FromSeconds(3));
                await WaitForPublicationAsync(directUri);

                // Recovery verifies the URL again, but a foreign proof still revokes trust immediately.
                ownerRegistration ??= registry.Register(ownerId, sharedUri);
                Volatile.Write(ref probeMode, 0);
                await WaitForPublicationAsync(sharedUri);
                Volatile.Write(ref probeMode, 2);
                await WaitForPublicationAsync(directUri);
                Volatile.Write(ref probeMode, 1);
                Assert.That(await publications.Reader.ReadAsync(cancellation.Token), Is.EqualTo(directUri),
                    "An invalid proof must clear the previous grace period.");
            }
            finally
            {
                await cancellation.CancelAsync();
                await monitor.WaitAsync(TimeSpan.FromSeconds(5));
            }

            async Task WaitForPublicationAsync(Uri expected)
            {
                while (await publications.Reader.ReadAsync(cancellation.Token) != expected) { }
            }
        }
        finally
        {
            ownerRegistration?.Dispose();
            await listener.StopAsync();
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Volatile.Read(ref _ticks);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
}
