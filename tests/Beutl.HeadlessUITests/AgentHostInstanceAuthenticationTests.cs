using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Headless.NUnit;
using Beutl.AgentHost;
using Beutl.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AgentHostInstanceAuthenticationTests
{
    [Test]
    public void Identity_proofs_require_the_right_secret_challenge_and_instance_and_are_not_credentials()
    {
        const string secret = "identity-test-secret";
        string firstId = Guid.NewGuid().ToString("N");
        string secondId = Guid.NewGuid().ToString("N");
        var first = new AgentHostInstanceAuthentication(secret, firstId);
        var second = new AgentHostInstanceAuthentication(secret, secondId);
        var differentProfile = new AgentHostInstanceAuthentication("other-secret", firstId);
        string challenge = AgentHostInstanceAuthentication.CreateChallenge();
        AgentHostIdentityProof proof = second.CreateProof(challenge);
        string credential = first.CreateForwardToken(secondId);
        Assert.Multiple(() =>
        {
            Assert.That(first.VerifyProof(secondId, challenge, proof), Is.True);
            Assert.That(first.VerifyProof(firstId, challenge, proof), Is.False);
            Assert.That(first.VerifyProof(secondId, AgentHostInstanceAuthentication.CreateChallenge(), proof), Is.False);
            Assert.That(differentProfile.VerifyProof(secondId, challenge, proof), Is.False);
            Assert.That(second.IsForwardToken(credential), Is.True);
            Assert.That(first.IsForwardToken(credential), Is.False);
            Assert.That(second.IsForwardToken(proof.Proof), Is.False);
            Assert.That(credential, Is.Not.EqualTo(secret));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Replacement_listener_never_receives_the_shared_token_even_after_a_valid_proof(bool proveIdentity)
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(Path.GetTempPath(), "beutl-peer-auth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await using var source = CreateHost(directory);
        await using var target = CreateHost(directory);
        try
        {
            await source.StartAsync();
            await target.StartAsync();
            int port = target.EndpointUri!.Port;
            string path = Path.Combine(directory, target.InstanceId + ".json");
            string stale = File.ReadAllText(path);
            await target.StopAsync();
            var observed = new ConcurrentQueue<string>();
            var authentication = new AgentHostInstanceAuthentication(AgentHostInstanceTestWorker.Token, target.InstanceId);
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
            await using WebApplication replacement = builder.Build();
            replacement.Run(async context =>
            {
                observed.Enqueue(context.Request.Headers.Authorization.ToString());
                if (proveIdentity && context.Request.Path == "/agent-host/identity")
                {
                    // A valid proof followed by a hostile response models a listener replaced
                    // between the proof and the next request. The credential must remain scoped.
                    await context.Response.WriteAsJsonAsync(authentication.CreateProof(
                        context.Request.Query["challenge"].ToString()));
                }
                else
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
            });
            await replacement.StartAsync();
            try
            {
                await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = source.EndpointUri!,
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + source.Token }
                }));
                File.WriteAllText(path, stale);
                CallToolResult discovery = await client.CallToolAsync("list_instances");
                JsonObject result = JsonNode.Parse(discovery.Content.OfType<TextContentBlock>().First().Text)!.AsObject();
                Assert.That(result["value"]!["instances"]!.AsArray(), Has.Count.EqualTo(1));
                if (!proveIdentity) Assert.That(File.Exists(path), Is.False);
                File.WriteAllText(path, stale); // Exercise forwarding independently of discovery cleanup.
                CallToolResult forwarded = await client.CallToolAsync("read_operation_status",
                    new Dictionary<string, object?> { ["instanceId"] = target.InstanceId });
                JsonObject failure = JsonNode.Parse(forwarded.Content.OfType<TextContentBlock>().First().Text)!.AsObject();
                Assert.That(failure["error"]!["code"]!.GetValue<string>(), Is.EqualTo("instance_unavailable"));
                Assert.That(observed, Does.Not.Contain("Bearer " + source.Token));
                Assert.That(observed.First(), Is.Empty, "The identity proof must be requested without credentials.");
                string[] credentials = observed.Where(value => value.Length > 0).ToArray();
                if (proveIdentity)
                {
                    Assert.That(credentials, Is.Not.Empty);
                    using var http = new HttpClient();
                    http.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(credentials[0]);
                    using HttpResponseMessage replay = await http.GetAsync(source.EndpointUri);
                    Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
                        "A credential captured after port replacement cannot authorize another host.");
                }
                else
                    Assert.That(credentials, Is.Empty);
            }
            finally { await replacement.StopAsync(); }
        }
        finally
        {
            await target.StopAsync();
            await source.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTest]
    public async Task A_failed_registration_keeps_direct_MCP_and_local_discovery_available()
    {
        await TestReset.ResetShellAsync();
        string directory = Path.Combine(Path.GetTempPath(), "beutl-registration-failure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string blocked = Path.Combine(directory, "agent-hosts");
        File.WriteAllText(blocked, "A file prevents creation of the discovery directory.");
        await using var endpoint = CreateHost(blocked);
        try
        {
            await endpoint.StartAsync();
            Assert.That(endpoint.IsRunning, Is.True);
            await using McpClient client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint.EndpointUri!,
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + endpoint.Token }
            }));
            CallToolResult status = await client.CallToolAsync("read_operation_status");
            Assert.That(JsonNode.Parse(status.Content.OfType<TextContentBlock>().First().Text)!["isSuccess"]!.GetValue<bool>(), Is.True);
            CallToolResult result = await client.CallToolAsync("list_instances");
            JsonArray instances = JsonNode.Parse(result.Content.OfType<TextContentBlock>().First().Text)!["value"]!["instances"]!.AsArray();
            Assert.That(instances.Single()!["instanceId"]!.GetValue<string>(), Is.EqualTo(endpoint.InstanceId));
        }
        finally
        {
            await endpoint.StopAsync();
            Directory.Delete(directory, true);
        }
    }

    private static AgentHostEndpoint CreateHost(string directory)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new AgentHostEndpoint(new ProjectService(), new EditorService(TestShell.Extensions),
            port, AgentHostInstanceTestWorker.Token, registryDirectory: directory);
    }
}
