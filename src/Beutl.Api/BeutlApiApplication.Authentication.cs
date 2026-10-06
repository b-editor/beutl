using System.Diagnostics;
using System.Net.Http.Headers;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Activity = System.Diagnostics.Activity;

namespace Beutl.Api;

public partial class BeutlApiApplication
{
    private void HandleAuthenticatedUserChanged(AuthenticatedUser? user)
    {
        CancellationTokenSource? previousSession;
        lock (_authenticationGate)
        {
            previousSession = _authenticationSessionCts;
            _authenticationSessionCts = user is null ? null : new CancellationTokenSource();
            _authenticationGeneration++;
            _authenticationAttemptVersion++;
            if (user is null)
            {
                _httpClient.DefaultRequestHeaders.Authorization = null;
            }
            else
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", user.Token);
            }
        }

        previousSession?.Cancel();
        previousSession?.Dispose();
    }

    private long BeginAuthenticationAttempt()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_authenticationGate)
            return ++_authenticationAttemptVersion;
    }

    private void CommitAuthenticatedUser(
        AuthenticatedUser user,
        long authenticationAttempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_authenticationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_authenticationAttemptVersion != authenticationAttempt)
                throw new AuthenticationRequiredException();

            _authenticatedUser.Value = user;
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", user.Token);
        }
    }

    private bool IsAuthenticationSessionCurrent(AuthenticatedUser user, long generation)
    {
        return !_disposed
            && generation == _authenticationGeneration
            && _authenticationSessionCts is not null
            && ReferenceEquals(_authenticatedUser.Value, user);
    }

    internal async Task<AuthenticatedApiResult<T>> SendAuthenticatedAsync<T>(
        Func<string, CancellationToken, Task<T>> send,
        CancellationToken cancellationToken,
        AuthenticatedUser? expectedUser = null)
    {
        ArgumentNullException.ThrowIfNull(send);
        cancellationToken.ThrowIfCancellationRequested();
        expectedUser ??= AiAuthenticatedRequestScope.Current;

        using AuthenticatedSessionContext context = await CreateAuthenticatedSessionAsync(
                expectedUser,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            T value = await send(context.Authorization, context.CancellationToken).ConfigureAwait(false);
            try
            {
                EnsureAuthenticationSessionCurrent(context, cancellationToken);
            }
            catch (AuthenticationRequiredException ex)
            {
                // The endpoint already returned. Its job may have been reserved even though the
                // authentication generation changed before the caller could observe the result.
                throw new AuthenticationRequiredException(
                    currentAttemptReservationIsKnownAbsent: false,
                    ex);
            }
            return new AuthenticatedApiResult<T>(value, context.User);
        }
        catch (OperationCanceledException) when (
            context.AuthenticationToken.IsCancellationRequested
            && !context.ApplicationToken.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested)
        {
            // Cancellation can race with the server accepting the request. Keep any idempotency
            // key until an authoritative endpoint response says that no job was reserved.
            throw new AuthenticationRequiredException(
                currentAttemptReservationIsKnownAbsent: false);
        }
    }

    private async ValueTask<AuthenticatedSessionContext> CreateAuthenticatedSessionAsync(
        AuthenticatedUser? expectedUser,
        CancellationToken cancellationToken)
    {
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = lifetimeCts.Token;
        AuthenticatedUser user;
        lock (_authenticationGate)
        {
            user = _authenticatedUser.Value ?? throw new AuthenticationRequiredException();
            if (expectedUser is not null && !ReferenceEquals(user, expectedUser))
                throw new AuthenticationRequiredException();
        }

        await user.RefreshAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_authenticationGate)
            {
                if (!ReferenceEquals(_authenticatedUser.Value, user)
                    || _authenticationSessionCts is null)
                {
                    throw new AuthenticationRequiredException();
                }

                long generation = _authenticationGeneration;
                CancellationToken authenticationToken = _authenticationSessionCts.Token;
                CancellationToken applicationToken = _lifetimeCts.Token;
                var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    applicationToken,
                    authenticationToken);
                return new AuthenticatedSessionContext(
                    user,
                    generation,
                    $"Bearer {user.Token}",
                    authenticationToken,
                    applicationToken,
                    linkedCancellation);
            }
        }
    }

    private void EnsureAuthenticationSessionCurrent(
        AuthenticatedSessionContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.ApplicationToken.ThrowIfCancellationRequested();
        lock (_authenticationGate)
        {
            if (!IsAuthenticationSessionCurrent(context.User, context.Generation))
                throw new AuthenticationRequiredException();
        }
    }

    internal void CommitForAuthenticatedUser(
        AuthenticatedUser user,
        Action commit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(commit);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_authenticationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!ReferenceEquals(_authenticatedUser.Value, user)
                || _authenticationSessionCts is null)
            {
                throw new AuthenticationRequiredException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            commit();
        }
    }

    public void SignOut(bool deleteFile = true)
    {
        lock (_authenticationGate)
        {
            _authenticationAttemptVersion++;
            _authenticatedUser.Value = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
        if (deleteFile)
        {
            string fileName = Path.Combine(Helper.AppRoot, UserFileName);
            if (File.Exists(fileName))
            {
                File.Delete(fileName);
            }
        }
    }

    internal async ValueTask RefreshAuthenticatedUserAsync(
        AuthenticatedUser user,
        bool force,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        using CancellationTokenSource lifetimeCts = CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken applicationToken = lifetimeCts.Token;
        long authenticationGeneration;
        CancellationToken sessionToken;
        CancellationTokenSource linkedCts;
        lock (_authenticationGate)
        {
            authenticationGeneration = _authenticationGeneration;
            if (_disposed
                || !ReferenceEquals(_authenticatedUser.Value, user)
                || _authenticationSessionCts is null)
            {
                throw new AuthenticationRequiredException();
            }

            sessionToken = _authenticationSessionCts.Token;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                applicationToken,
                sessionToken);
        }

        using (linkedCts)
        {
            CancellationToken token = linkedCts.Token;
            bool gateEntered = false;
            try
            {
                await _authenticationRefreshGate.WaitAsync(token).ConfigureAwait(false);
                gateEntered = true;
                token.ThrowIfCancellationRequested();
                lock (_authenticationGate)
                {
                    if (!IsAuthenticationSessionCurrent(user, authenticationGeneration))
                        throw new AuthenticationRequiredException();
                }

                using Activity? activity = ActivitySource.StartActivity(
                    "AuthenticatedUser.Refresh",
                    ActivityKind.Client);
                (AuthResponse response, DateTime writeTime) = user.GetAuthenticationState();
                string fileName = Path.Combine(Helper.AppRoot, UserFileName);
                if (File.Exists(fileName))
                {
                    DateTime lastWriteTime = File.GetLastWriteTimeUtc(fileName);
                    if (writeTime < lastWriteTime)
                    {
                        AuthenticatedUser? fileUser = await ReadUserAsync(token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        if (fileUser?.Profile.Id == user.Profile.Id)
                        {
                            (response, writeTime) = fileUser.GetAuthenticationState();
                        }
                        else if (fileUser is not null)
                        {
                            SignOutIfCurrent(user);
                            throw new InvalidOperationException(
                                "The user may have been changed in another process.");
                        }
                    }
                }

                bool isExpired = response.Expiration < DateTime.UtcNow;
                activity?.SetTag("force", force);
                activity?.SetTag("is_expired", isExpired);
                bool refreshed = false;
                if (force || isExpired)
                {
                    response = await Account.Refresh(
                            new RefreshTokenRequest
                            {
                                RefreshToken = response.RefreshToken,
                                Token = response.Token,
                            },
                            token)
                        .ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    refreshed = true;
                    activity?.AddEvent(new("Refreshed"));
                }

                lock (_authenticationGate)
                {
                    token.ThrowIfCancellationRequested();
                    if (!IsAuthenticationSessionCurrent(user, authenticationGeneration))
                        throw new AuthenticationRequiredException();

                    user.CommitAuthenticationState(response, writeTime);
                    _httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", response.Token);
                }

                if (refreshed)
                {
                    SaveUser(user);
                    activity?.AddEvent(new("Saved"));
                }
            }
            catch (OperationCanceledException) when (
                sessionToken.IsCancellationRequested
                && !applicationToken.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
            {
                throw new AuthenticationRequiredException();
            }
            finally
            {
                if (gateEntered)
                {
                    _authenticationRefreshGate.Release();
                }
            }
        }
    }

    private void SignOutIfCurrent(AuthenticatedUser user)
    {
        lock (_authenticationGate)
        {
            if (!ReferenceEquals(_authenticatedUser.Value, user))
                return;

            _authenticationAttemptVersion++;
            _authenticatedUser.Value = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
        }
    }
}
