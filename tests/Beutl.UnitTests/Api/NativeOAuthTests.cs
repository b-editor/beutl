using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Beutl.Api;
using Beutl.Api.Clients;

namespace Beutl.UnitTests.Api;

[TestFixture]
public class NativeOAuthTests
{
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/')
        .PadRight((value.Length + 3) / 4 * 4, '='));

    [Test]
    public void Authorization_UsesS256AndPublicNativeClientWithoutASecret()
    {
        NativeAuthorization authorization = NativeOAuthClient.CreateAuthorization("https://example.test", "http://127.0.0.1:42000/__/auth/handler/");
        var query = System.Web.HttpUtility.ParseQueryString(authorization.Uri.Query);
        Assert.Multiple(() =>
        {
            Assert.That(query["client_id"], Is.EqualTo("beutl-desktop"));
            Assert.That(query["client_secret"], Is.Null);
            Assert.That(query["code_challenge_method"], Is.EqualTo("S256"));
            Assert.That(query["code_challenge"], Is.EqualTo(NativeProofKey.Hash(authorization.Verifier)));
            Assert.That(query["dpop_jkt"], Is.EqualTo(NativeProofKey.Thumbprint(authorization.PrivateKey)));
            Assert.That(query["state"], Is.EqualTo(authorization.State));
            Assert.That(authorization.State, Is.Not.EqualTo(NativeOAuthClient.CreateAuthorization("https://example.test", "http://127.0.0.1:42000/__/auth/handler/").State));
        });
    }

    [Test]
    public void Proof_IsVerifiableWithOnlyThePublicKeyAndBindsTheToken()
    {
        string key = NativeProofKey.Create();
        string[] parts = NativeProofKey.Sign(key, HttpMethod.Get, new Uri("https://example.test/api/v3/user?x=1#fragment"), "access-token").Split('.');
        using var header = JsonDocument.Parse(Decode(parts[0]));
        using var claims = JsonDocument.Parse(Decode(parts[1]));
        JsonElement jwk = header.RootElement.GetProperty("jwk");
        using var publicKey = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = Decode(jwk.GetProperty("x").GetString()!), Y = Decode(jwk.GetProperty("y").GetString()!) },
        });
        Assert.Multiple(() =>
        {
            Assert.That(publicKey.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation), Is.True);
            Assert.That(jwk.TryGetProperty("d", out _), Is.False);
            Assert.That(header.RootElement.GetProperty("typ").GetString(), Is.EqualTo("dpop+jwt"));
            Assert.That(claims.RootElement.GetProperty("htu").GetString(), Is.EqualTo("https://example.test/api/v3/user"));
            Assert.That(claims.RootElement.GetProperty("ath").GetString(), Is.EqualTo(NativeProofKey.Hash("access-token")));
            Assert.That(claims.RootElement.GetProperty("htm").GetString(), Is.EqualTo("GET"));
        });
    }

    [Test]
    public async Task Transport_SignsEveryRequestWithoutDisposingItsUpload()
    {
        var proofs = new List<string>();
        using var transport = new HttpClient(new CallbackHandler(async request =>
        {
            proofs.Add(request.Headers.GetValues("DPoP").Single());
            Assert.That(await request.Content!.ReadAsStringAsync(), Is.EqualTo("upload"));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var auth = new AuthResponse { Token = "access-token", RefreshToken = "refresh-token", Expiration = DateTime.UtcNow.AddMinutes(5), DpopPrivateKey = NativeProofKey.Create() };
        using var client = new HttpClient(new NativeProofHandler(transport, () => auth, new Uri("https://example.test")));
        for (int i = 0; i < 2; i++)
        {
            using var content = new StringContent("upload");
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://example.test/api/upload") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("DPoP", auth.Token);
            using var response = await client.SendAsync(request);
            Assert.That(await content.ReadAsStringAsync(), Is.EqualTo("upload"));
        }
        Assert.That(proofs[0], Is.Not.EqualTo(proofs[1]));
    }

    [Test]
    public async Task CodeExchange_DoesNotSendThePreviousAccountsAuthorization()
    {
        NativeAuthorization authorization = NativeOAuthClient.CreateAuthorization("https://example.test", "http://127.0.0.1:42000/__/auth/handler/");
        using var transport = new HttpClient(new CallbackHandler(async request =>
        {
            Assert.That(request.Headers.Authorization, Is.Null);
            var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync());
            Assert.That(form["code_verifier"], Is.EqualTo(authorization.Verifier));
            using var claims = JsonDocument.Parse(Decode(request.Headers.GetValues("DPoP").Single().Split('.')[1]));
            Assert.That(claims.RootElement.TryGetProperty("ath", out _), Is.False);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"new-access","refresh_token":"new-refresh","token_type":"DPoP","expires_in":900}""") };
        }));
        using var client = new HttpClient(new NativeProofHandler(transport, () => null, new Uri("https://example.test"))) { BaseAddress = new Uri("https://example.test") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "old-account-token");
        var response = await NativeOAuthClient.ExchangeAsync(client, authorization, "code", CancellationToken.None);
        Assert.That(response.DpopPrivateKey, Is.EqualTo(authorization.PrivateKey));
        Assert.That(response.AuthorizationScheme, Is.EqualTo("DPoP"));
    }

    [Test]
    public async Task Loopback_RejectsWrongStateOrIssuerAndWaitsForTheMatchingCallback()
    {
        string uri = $"http://127.0.0.1:{LoopbackAuthorizationListener.GetRandomUnusedPort()}/__/auth/handler/";
        using var listener = LoopbackAuthorizationListener.StartListener(uri);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<string> code = LoopbackAuthorizationListener.GetResponseFromListener(listener, "expected", "https://example.test/api/auth", timeout.Token);
        using var client = new HttpClient();
        using var invalidState = await client.GetAsync($"{uri}?state=wrong&iss=https%3A%2F%2Fexample.test%2Fapi%2Fauth&code=stolen", timeout.Token);
        Assert.That(invalidState.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var invalidIssuer = await client.GetAsync($"{uri}?state=expected&iss=https%3A%2F%2Fother.test&code=stolen", timeout.Token);
        Assert.That(invalidIssuer.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var valid = await client.GetAsync($"{uri}?state=expected&iss=https%3A%2F%2Fexample.test%2Fapi%2Fauth&code=valid", timeout.Token);
        Assert.That(await code, Is.EqualTo("valid"));
    }

    private sealed class CallbackHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }
}
