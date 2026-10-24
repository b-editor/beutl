using Beutl.Api.Clients;

namespace Beutl.Api;

// Wrap the supplied client so existing transports, test handlers and streaming behavior are retained.
internal sealed class NativeProofHandler(HttpClient transport, Func<AuthResponse?> currentAuthentication, Uri origin) : HttpMessageHandler
{
    internal static readonly HttpRequestOptionsKey<string> ProofKey = new("Beutl.DPoP.PrivateKey");
    internal static readonly HttpRequestOptionsKey<bool> TokenRequest = new("Beutl.OAuth.TokenRequest");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Content = request.Content,
            Version = request.Version,
            VersionPolicy = request.VersionPolicy,
        };
        foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
        foreach (var option in request.Options) forwarded.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
        try
        {
            bool tokenRequest = request.Options.TryGetValue(TokenRequest, out bool value) && value;
            request.Options.TryGetValue(ProofKey, out string? privateKey);
            var authorization = forwarded.Headers.Authorization;
            if (!tokenRequest && authorization?.Scheme.Equals("DPoP", StringComparison.OrdinalIgnoreCase) == true)
            {
                AuthResponse? auth = currentAuthentication();
                privateKey ??= auth?.Token == authorization.Parameter ? auth.DpopPrivateKey : null;
                if (privateKey is null) throw new InvalidOperationException("The device key for this token is unavailable.");
            }
            if (privateKey is not null)
            {
                Uri uri = request.RequestUri ?? throw new InvalidOperationException("Missing request URI.");
                if (uri.GetLeftPart(UriPartial.Authority) != origin.GetLeftPart(UriPartial.Authority))
                    throw new InvalidOperationException("Device credentials cannot be sent to another server.");
                if (tokenRequest) forwarded.Headers.Authorization = null;
                forwarded.Headers.Remove("DPoP");
                forwarded.Headers.TryAddWithoutValidation("DPoP", NativeProofKey.Sign(privateKey, request.Method, uri,
                    tokenRequest ? null : authorization?.Parameter));
            }
            return await transport.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The original request owns its content, including an upload stream.
            forwarded.Content = null;
        }
    }
}
