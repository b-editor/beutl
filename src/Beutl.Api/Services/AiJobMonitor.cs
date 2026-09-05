using System.Collections.Immutable;
using System.Net;
using System.Reactive;
using System.Reactive.Disposables;
using Beutl.Api.Objects;
using Reactive.Bindings;
using Refit;

namespace Beutl.Api.Services;

public sealed record AiJobMonitorSnapshot(
    ImmutableArray<AiJob> Jobs,
    string? NextCursor,
    bool IsLoading,
    Exception? Error)
{
    public static AiJobMonitorSnapshot Empty { get; } = new([], null, false, null);
}

internal sealed class AiJobMonitor : IAiJobMonitor, IDisposable
{
    private readonly IAiJobClient _client;
    private readonly IAiJobKindRegistry _jobKinds;
    private readonly BeutlApiApplication _application;
    private readonly TimeSpan _pollInterval;
    private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _pollingGate = new();
    private readonly object _stateGate = new();
    private readonly ReactivePropertySlim<AiJobMonitorSnapshot> _snapshot =
        new(AiJobMonitorSnapshot.Empty);
    private readonly ReadOnlyReactivePropertySlim<AiJobMonitorSnapshot> _readOnlySnapshot;
    private readonly CompositeDisposable _subscriptions = [];
    private CancellationTokenSource? _authenticationCts;
    private CancellationTokenSource? _pollingCts;
    private long _authenticationVersion;
    private int _pollingLeases;
    private readonly HashSet<long> _scheduledRetryVersions = [];
    private volatile bool _disposed;

    public AiJobMonitor(BeutlApiApplication application)
        : this(
            application,
            application.GetResource<IAiJobClient>(),
            application.GetResource<IAiJobKindRegistry>(),
            application.GetResource<AiJobChangeNotifier>().Changes,
            TimeSpan.FromSeconds(5))
    {
    }

    internal AiJobMonitor(
        BeutlApiApplication application,
        IAiJobClient client,
        IAiJobKindRegistry jobKinds,
        IObservable<Unit> jobChanges,
        TimeSpan pollInterval,
        Func<TimeSpan, CancellationToken, Task>? retryDelay = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(jobKinds);
        ArgumentNullException.ThrowIfNull(jobChanges);
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));

        _application = application;
        _client = client;
        _jobKinds = jobKinds;
        _pollInterval = pollInterval;
        _retryDelay = retryDelay ?? Task.Delay;
        _readOnlySnapshot = _snapshot.ToReadOnlyReactivePropertySlim(AiJobMonitorSnapshot.Empty);
        _subscriptions.Add(jobChanges.Subscribe(HandleJobsChanged));
        _subscriptions.Add(application.AuthenticatedUser.Subscribe(HandleAuthenticatedUserChanged));
    }

    public IReadOnlyReactiveProperty<AiJobMonitorSnapshot> Snapshot => _readOnlySnapshot;

    public IDisposable AcquirePolling()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_pollingGate)
        {
            _pollingLeases++;
        }

        EnsurePolling();
        if (!GetSnapshot().IsLoading)
        {
            RequestRefreshForCurrentAuthentication();
        }

        return Disposable.Create(ReleasePolling);
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
        => RefreshCoreAsync(append: false, cancellationToken);

    public Task LoadNextPageAsync(CancellationToken cancellationToken)
        => RefreshCoreAsync(append: true, cancellationToken);

    internal Task RefreshPollingAsync(CancellationToken cancellationToken)
        => RefreshCoreAsync(append: false, cancellationToken, preserveLoadedTail: true);

    public void Dispose()
    {
        CancellationTokenSource? authenticationCts;
        CancellationTokenSource? pollingCts;
        lock (_stateGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            authenticationCts = _authenticationCts;
            _authenticationCts = null;
        }

        lock (_pollingGate)
        {
            pollingCts = _pollingCts;
        }

        authenticationCts?.Cancel();
        pollingCts?.Cancel();
        _subscriptions.Dispose();
        _readOnlySnapshot.Dispose();
        _snapshot.Dispose();
        authenticationCts?.Dispose();
    }

    private async Task RefreshCoreAsync(
        bool append,
        CancellationToken cancellationToken,
        bool preserveLoadedTail = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetAuthenticationContext(
                out AuthenticatedUser? expectedOwner,
                out long expectedVersion,
                out CancellationToken authenticationToken))
        {
            SetAuthenticationRequiredSnapshot();
            return;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            authenticationToken);
        CancellationToken token = linkedCts.Token;
        bool gateEntered = false;
        try
        {
            await _refreshGate.WaitAsync(token);
            gateEntered = true;
            token.ThrowIfCancellationRequested();

            AiJobMonitorSnapshot previous;
            string? cursor;
            lock (_stateGate)
            {
                if (!IsCurrentAuthentication(expectedOwner!, expectedVersion))
                    return;

                previous = _snapshot.Value;
                cursor = append ? previous.NextCursor : null;
                if (append && cursor is null)
                    return;

                _snapshot.Value = previous with { IsLoading = true, Error = null };
            }

            try
            {
                AiJobPage page = await _client.GetPageAsync(new AiJobPageRequest(cursor), token);
                ImmutableArray<AiJob> jobs = MergeJobs(
                    append || preserveLoadedTail ? previous.Jobs : [],
                    page.Jobs);

                lock (_stateGate)
                {
                    if (IsCurrentAuthentication(expectedOwner!, expectedVersion))
                    {
                        _snapshot.Value = new AiJobMonitorSnapshot(
                            jobs,
                            page.NextCursor,
                            false,
                            null);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                lock (_stateGate)
                {
                    if (IsCurrentAuthentication(expectedOwner!, expectedVersion))
                    {
                        _snapshot.Value = previous with { IsLoading = false };
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    throw;
                return;
            }
            catch (Exception ex)
            {
                lock (_stateGate)
                {
                    if (IsCurrentAuthentication(expectedOwner!, expectedVersion))
                    {
                        _snapshot.Value = previous with { IsLoading = false, Error = ex };
                    }
                }
                if (!append && previous.Jobs.IsEmpty && IsTransientRefreshFailure(ex))
                    ScheduleRetry(expectedOwner!, expectedVersion, authenticationToken);
            }

            EnsurePolling();
        }
        catch (OperationCanceledException) when (
            authenticationToken.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (gateEntered)
            {
                _refreshGate.Release();
            }
        }
    }

    private void ScheduleRetry(
        AuthenticatedUser owner,
        long authenticationVersion,
        CancellationToken authenticationToken)
    {
        lock (_stateGate)
        {
            if (_disposed || !_scheduledRetryVersions.Add(authenticationVersion))
                return;
        }

        _ = Task.Run(async () =>
        {
            bool releasedSchedule = false;
            try
            {
                await _retryDelay(_pollInterval, authenticationToken);
                lock (_stateGate)
                {
                    _scheduledRetryVersions.Remove(authenticationVersion);
                }
                releasedSchedule = true;
                if (IsCurrentAuthentication(owner, authenticationVersion))
                    await RefreshCoreAsync(false, authenticationToken);
            }
            catch (OperationCanceledException) when (authenticationToken.IsCancellationRequested)
            {
            }
            finally
            {
                if (!releasedSchedule)
                {
                    lock (_stateGate)
                    {
                        _scheduledRetryVersions.Remove(authenticationVersion);
                    }
                }
            }
        }, CancellationToken.None);
    }

    private static bool IsTransientRefreshFailure(Exception exception)
        => exception switch
        {
            TaskCanceledException => true,
            TimeoutException => true,
            HttpRequestException { StatusCode: null } => true,
            HttpRequestException { StatusCode: { } status } => IsRetryableStatus(status),
            ApiException apiException => IsRetryableStatus(apiException.StatusCode),
            _ => false,
        };

    private static bool IsRetryableStatus(HttpStatusCode status)
        => status is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            || (int)status >= 500;

    private static ImmutableArray<AiJob> MergeJobs(
        IEnumerable<AiJob> existing,
        IEnumerable<AiJob> incoming)
    {
        var result = new List<AiJob>();
        var indices = new Dictionary<AiJobId, int>();
        foreach (AiJob job in existing.Concat(incoming))
        {
            if (indices.TryGetValue(job.Id, out int index))
            {
                result[index] = job;
            }
            else
            {
                indices.Add(job.Id, result.Count);
                result.Add(job);
            }
        }

        return result.ToImmutableArray();
    }

    private bool ShouldPoll(AiJob job)
    {
        try
        {
            return _jobKinds.GetStatus(job).ShouldPoll;
        }
        catch
        {
            return false;
        }
    }

    private AiJobMonitorSnapshot GetSnapshot()
    {
        lock (_stateGate)
        {
            return _snapshot.Value;
        }
    }

    private void HandleJobsChanged(Unit value)
        => RequestRefreshForCurrentAuthentication();

    private void HandleAuthenticatedUserChanged(AuthenticatedUser? user)
    {
        CancellationTokenSource? previousCts;
        CancellationTokenSource? nextCts = user is null ? null : new CancellationTokenSource();
        lock (_stateGate)
        {
            if (_disposed)
            {
                nextCts?.Dispose();
                return;
            }

            previousCts = _authenticationCts;
            _authenticationCts = nextCts;
            _authenticationVersion++;
            _snapshot.Value = user is null
                ? CreateAuthenticationRequiredSnapshot()
                : AiJobMonitorSnapshot.Empty with { IsLoading = true };
        }

        previousCts?.Cancel();
        previousCts?.Dispose();
        if (user is not null)
        {
            RequestRefreshForCurrentAuthentication();
            EnsurePolling();
        }
        else
        {
            CancelPollingIfUnneeded();
        }
    }

    private void RequestRefreshForCurrentAuthentication()
    {
        CancellationToken token;
        lock (_stateGate)
        {
            if (_disposed || _authenticationCts is null)
                return;
            token = _authenticationCts.Token;
        }

        _ = RefreshFromNotificationAsync(token);
    }

    private async Task RefreshFromNotificationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool TryGetAuthenticationContext(
        out AuthenticatedUser? owner,
        out long authenticationVersion,
        out CancellationToken authenticationToken)
    {
        lock (_stateGate)
        {
            owner = _application.AuthenticatedUser.Value;
            authenticationVersion = _authenticationVersion;
            if (_disposed || owner is null || _authenticationCts is null)
            {
                authenticationToken = new CancellationToken(canceled: true);
                return false;
            }

            authenticationToken = _authenticationCts.Token;
            return true;
        }
    }

    private bool IsCurrentAuthentication(AuthenticatedUser owner, long authenticationVersion)
        => !_disposed
            && _authenticationCts is not null
            && _authenticationVersion == authenticationVersion
            && ReferenceEquals(_application.AuthenticatedUser.Value, owner);

    private void SetAuthenticationRequiredSnapshot()
    {
        lock (_stateGate)
        {
            if (!_disposed)
            {
                _snapshot.Value = CreateAuthenticationRequiredSnapshot();
            }
        }
    }

    private static AiJobMonitorSnapshot CreateAuthenticationRequiredSnapshot()
        => AiJobMonitorSnapshot.Empty with { Error = new AuthenticationRequiredException() };

    private void ReleasePolling()
    {
        lock (_pollingGate)
        {
            if (_pollingLeases == 0)
                return;
            _pollingLeases--;
        }

        CancelPollingIfUnneeded();
    }

    private void EnsurePolling()
    {
        CancellationToken authenticationToken;
        lock (_stateGate)
        {
            if (_disposed || _authenticationCts is null)
                return;
            authenticationToken = _authenticationCts.Token;
        }

        lock (_pollingGate)
        {
            if (_pollingCts is not null
                || (_pollingLeases == 0 && !GetSnapshot().Jobs.Any(ShouldPoll)))
            {
                return;
            }

            var cancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(authenticationToken);
            _pollingCts = cancellationTokenSource;
            _ = RunPollingAsync(cancellationTokenSource);
        }
    }

    private void CancelPollingIfUnneeded()
    {
        CancellationTokenSource? cancellationTokenSource = null;
        lock (_pollingGate)
        {
            if (_pollingLeases == 0 && !GetSnapshot().Jobs.Any(ShouldPoll))
            {
                cancellationTokenSource = _pollingCts;
            }
        }

        cancellationTokenSource?.Cancel();
    }

    private async Task RunPollingAsync(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            await PollActiveJobsAsync(cancellationTokenSource.Token);
        }
        finally
        {
            lock (_pollingGate)
            {
                if (ReferenceEquals(_pollingCts, cancellationTokenSource))
                {
                    _pollingCts = null;
                }
            }

            cancellationTokenSource.Dispose();
            EnsurePolling();
        }
    }

    private bool ShouldKeepPolling()
    {
        lock (_pollingGate)
        {
            return !_disposed && (_pollingLeases > 0 || GetSnapshot().Jobs.Any(ShouldPoll));
        }
    }

    private async Task PollActiveJobsAsync(CancellationToken cancellationToken)
    {
        while (ShouldKeepPolling())
        {
            try
            {
                await Task.Delay(_pollInterval, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            AiJobMonitorSnapshot snapshot = GetSnapshot();
            ImmutableArray<AiJob> pollingJobs = snapshot.Jobs.Where(ShouldPoll).ToImmutableArray();
            foreach (AiJob job in pollingJobs)
            {
                if (!_jobKinds.TryAcquire(job.Kind, out IAiJobKindLease? kindLease))
                    continue;

                using (kindLease)
                {
                    AiJobKindDescriptor descriptor = kindLease.Descriptor;
                    AiJobStatusSemantics status;
                    try
                    {
                        status = descriptor.StatusResolver.Resolve(job.Status);
                    }
                    catch
                    {
                        continue;
                    }

                    if (!status.ShouldPoll || descriptor.RefreshHandler is not { } handler)
                    {
                        continue;
                    }

                    try
                    {
                        await handler.RefreshAsync(job, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (AuthenticationRequiredException)
                    {
                        break;
                    }
                    catch
                    {
                    }
                }
            }

            if (pollingJobs.Length > 0
                || snapshot.Error is not null and not AuthenticationRequiredException)
            {
                await RefreshPollingAsync(cancellationToken);
            }
        }
    }
}
