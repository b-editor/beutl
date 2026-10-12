namespace Beutl.UnitTests.TestInfrastructure;

// Answers each request with the responder and records the requested paths.
public sealed class DelegateHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder) : HttpMessageHandler
{
    private readonly object _requestsGate = new();
    private readonly List<string> _requests = [];

    public DelegateHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        : this((request, _) => Task.FromResult(responder(request)))
    {
    }

    public DelegateHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : this((request, _) => responder(request))
    {
    }

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requestsGate)
            {
                return _requests.ToArray();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        lock (_requestsGate)
        {
            _requests.Add(request.RequestUri?.PathAndQuery ?? string.Empty);
        }

        HttpResponseMessage response = await responder(request, cancellationToken);
        response.RequestMessage = request;
        return response;
    }
}
