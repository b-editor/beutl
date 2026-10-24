using System.Net;
using System.Net.Mime;
using System.Net.Sockets;

namespace Beutl.Api;

internal static class LoopbackAuthorizationListener
{
    public static int GetRandomUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    public static HttpListener StartListener(string redirectUri)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);
        listener.Start();
        return listener;
    }

    public static async Task<string> GetResponseFromListener(
        HttpListener listener, string expectedState, string expectedIssuer, CancellationToken ct)
    {
        using var registration = ct.Register(listener.Stop);
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(ct);
            }
            var query = context.Request.QueryString;
            string? code = query.Get("code");
            string? error = query.Get("error");
            if (context.Request.HttpMethod != "GET"
                || query.GetValues("state") is not { Length: 1 } || query.Get("state") != expectedState
                || query.GetValues("iss") is not { Length: 1 } || query.Get("iss") != expectedIssuer
                || (string.IsNullOrWhiteSpace(code) == string.IsNullOrWhiteSpace(error))
                || query.GetValues(code is null ? "error" : "code") is not { Length: 1 })
            {
                context.Response.StatusCode = 400;
                context.Response.Close();
                continue;
            }
            using Stream input = ReadClosePageResponse();
            context.Response.ContentType = MediaTypeNames.Text.Html;
            context.Response.ContentLength64 = input.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            await input.CopyToAsync(context.Response.OutputStream, ct).ConfigureAwait(false);
            context.Response.Close();
            if (error is not null) throw new InvalidOperationException("Device authorization was declined or failed.");
            return code!;
        }
    }

    private static Stream ReadClosePageResponse()
    {
        Stream? stream =
            typeof(BeutlApiApplication).Assembly.GetManifestResourceStream("Beutl.Api.Resources.index.html");

        return stream ?? throw new Exception("Embedded resource not found.");
    }
}
