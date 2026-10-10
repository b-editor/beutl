using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using Beutl.Logging;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Beutl.AgentHost;

// Add a configuration-backed listener to the existing Kestrel server. Its direct, code-backed
// listener is never changed, so existing connections and shared render/AI jobs stay in place.
internal sealed class AgentHostEndpointFailover : IDisposable
{
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_ownerProbeGracePeriod = TimeSpan.FromSeconds(5);
    private static readonly ILogger s_logger = Log.CreateLogger<AgentHostEndpointFailover>();
    private readonly AgentHostInstanceRegistry _registry;
    private readonly AgentHostInstanceAuthentication _authentication;
    private readonly TimeProvider _timeProvider;
    // The proof must come from the requested listener, never a proxy or redirect target.
    private readonly HttpClient _http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(1)
    };
    private readonly AliasConfigurationProvider _provider = new();
    private readonly IConfigurationRoot _configuration;
    private readonly int _preferredPort;
    private long? _lastVerifiedTimestamp;
    private int _attempt;

    public AgentHostEndpointFailover(AgentHostInstanceRegistry registry, string token, string instanceId, int preferredPort,
        TimeProvider? timeProvider = null)
    {
        _registry = registry;
        _authentication = new AgentHostInstanceAuthentication(token, instanceId);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _preferredPort = preferredPort;
        _configuration = new ConfigurationBuilder().Add(new AliasConfigurationSource(_provider)).Build();
    }

    public IConfiguration Configuration => _configuration;

    private Uri PreferredUri => new($"http://127.0.0.1:{_preferredPort}/mcp");

    public async Task<Uri> ResolveConnectionUriAsync(Uri directUri, CancellationToken cancellationToken)
        => SelectConnectionUri(directUri, directUri.Port == _preferredPort
            ? OwnerStatus.Compatible
            : await ProbeOwnerAsync(cancellationToken).ConfigureAwait(false));

    public async Task RunAsync(IServerAddressesFeature addresses, Uri directUri,
        Action<Uri> publishConnectionUri, CancellationToken cancellationToken)
    {
        if (directUri.Port == _preferredPort)
            return;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OwnerStatus owner = addresses.Addresses.Contains(PreferredUri.GetLeftPart(UriPartial.Authority))
                    ? OwnerStatus.Compatible
                    : await ProbeOwnerAsync(cancellationToken).ConfigureAwait(false);
                publishConnectionUri(SelectConnectionUri(directUri, owner));
                if (owner != OwnerStatus.Compatible && IsPreferredPortFree())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // Use a fresh endpoint name: a failed bind is remembered by Kestrel's
                    // configuration loader and an unchanged endpoint would not be retried.
                    _provider.SetAlias($"Preferred{++_attempt}", PreferredUri.GetLeftPart(UriPartial.Authority));
                }
                await Task.Delay(s_pollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Live MCP port failover stopped. The direct endpoint remains available.");
            publishConnectionUri(directUri);
        }
    }

    private Uri SelectConnectionUri(Uri directUri, OwnerStatus owner)
    {
        if (owner == OwnerStatus.Compatible)
        {
            _lastVerifiedTimestamp = _timeProvider.GetTimestamp();
            return PreferredUri;
        }
        // A different token or an invalid proof revokes trust immediately. A timeout or a
        // shutdown's disappearing registration must not persist a transient direct URL in settings.
        if (owner == OwnerStatus.Incompatible)
            _lastVerifiedTimestamp = null;
        return _lastVerifiedTimestamp is { } timestamp
            && _timeProvider.GetElapsedTime(timestamp) < s_ownerProbeGracePeriod
            ? PreferredUri
            : directUri;
    }

    private async Task<OwnerStatus> ProbeOwnerAsync(CancellationToken cancellationToken)
    {
        AgentHostInstanceRegistration[] registrations = _registry.Read().ToArray();
        if (registrations.Length == 0)
            return OwnerStatus.Unavailable;
        string challenge = AgentHostInstanceAuthentication.CreateChallenge();
        try
        {
            // Never send a bearer token to a port merely because it is occupied. The public
            // challenge must prove both a registered instance identity and the shared secret.
            AgentHostIdentityProof? proof = await _http.GetFromJsonAsync<AgentHostIdentityProof>(
                new Uri(PreferredUri, "/agent-host/identity?challenge=" + challenge), cancellationToken)
                .ConfigureAwait(false);
            if (proof is null || !_authentication.VerifyProof(proof.InstanceId, challenge, proof))
                return OwnerStatus.Incompatible;
            // StopAsync removes discovery before releasing the listener. A valid proof from
            // that owner is transiently unavailable, rather than evidence of another profile.
            return registrations.Any(entry => entry.InstanceId == proof.InstanceId)
                ? OwnerStatus.Compatible
                : OwnerStatus.Unavailable;
        }
        catch (HttpRequestException ex)
        {
            return ex.StatusCode is null ? OwnerStatus.Unavailable : OwnerStatus.Incompatible;
        }
        catch (System.Text.Json.JsonException) { return OwnerStatus.Incompatible; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return OwnerStatus.Unavailable; }
    }

    private bool IsPreferredPortFree()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.ExclusiveAddressUse = true;
            socket.Bind(new IPEndPoint(IPAddress.Loopback, _preferredPort));
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        (_configuration as IDisposable)?.Dispose();
    }

    private enum OwnerStatus { Compatible, Unavailable, Incompatible }

    private sealed class AliasConfigurationSource(AliasConfigurationProvider provider) : IConfigurationSource
    {
        public IConfigurationProvider Build(IConfigurationBuilder builder) => provider;
    }

    private sealed class AliasConfigurationProvider : ConfigurationProvider
    {
        public void SetAlias(string name, string url)
        {
            Data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                [$"Endpoints:{name}:Url"] = url
            };
            OnReload();
        }
    }
}
