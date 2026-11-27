using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Editor.VersionControl;

namespace Beutl.Editor.Components.VersionControl.ViewModels;

internal sealed class HostedGitRemoteLookup
{
    private Request? _request;

    public void Reset() => _request = null;

    public void Update(string? origin, BeutlApiApplication? api, CancellationToken lifetime,
        Action<Action> postToUi, Action<HostedGitRemoteUrl?> apply)
    {
        var request = new Request(origin, api?.AuthenticatedUser.Value, lifetime);
        if (_request == request)
            return;

        _request = request;
        HostedGitRemoteUrl.TryParse(origin, out HostedGitRemoteUrl? localMatch);
        apply(localMatch);
        if (localMatch is null && origin is not null && api is not null && request.Owner is not null
            && !lifetime.IsCancellationRequested)
        {
            _ = ResolveAndPublishAsync(request, api, postToUi, apply);
        }
    }

    private async Task ResolveAndPublishAsync(Request request, BeutlApiApplication api,
        Action<Action> postToUi, Action<HostedGitRemoteUrl?> apply)
    {
        try
        {
            var (remote, retry) = await HostedGitRemoteUrl.ResolveAsync(request.Origin, api, request.Lifetime)
                .ConfigureAwait(false);
            if (remote is null && !retry)
                return;

            postToUi(() =>
            {
                if (ReferenceEquals(_request, request) && !request.Lifetime.IsCancellationRequested
                    && ReferenceEquals(request.Owner, api.AuthenticatedUser.Value))
                {
                    if (retry)
                        _request = null;
                    else
                        apply(remote);
                }
            });
        }
        catch (OperationCanceledException) when (request.Lifetime.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (request.Lifetime.IsCancellationRequested)
        {
        }
    }

    private sealed record Request(string? Origin, AuthenticatedUser? Owner, CancellationToken Lifetime);
}
