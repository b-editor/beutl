using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
    private int _loadedPageCount;
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

            if (!TryBeginRefresh(
                    expectedOwner!,
                    expectedVersion,
                    append,
                    out AiJobMonitorSnapshot? previous,
                    out int previousLoadedPageCount,
                    out string? cursor))
            {
                return;
            }

            try
            {
                (ImmutableArray<AiJob> jobs, string? nextCursor, int loadedPageCount) = await LoadJobsAsync(
                    cursor,
                    append,
                    preserveLoadedTail,
                    previous,
                    previousLoadedPageCount,
                    token);
                PublishIfCurrent(
                    expectedOwner!,
                    expectedVersion,
                    new AiJobMonitorSnapshot(
                        jobs,
                        nextCursor,
                        false,
                        null),
                    loadedPageCount);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                PublishIfCurrent(expectedOwner!, expectedVersion, previous with { IsLoading = false });

                if (cancellationToken.IsCancellationRequested)
                    throw;
                return;
            }
            catch (Exception ex)
            {
                PublishIfCurrent(expectedOwner!, expectedVersion, previous with { IsLoading = false, Error = ex });
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

    // Marks the current snapshot as loading; false when the authentication moved on or there is no next page.
    private bool TryBeginRefresh(
        AuthenticatedUser owner,
        long version,
        bool append,
        [NotNullWhen(true)] out AiJobMonitorSnapshot? previous,
        out int previousLoadedPageCount,
        out string? cursor)
    {
        lock (_stateGate)
        {
            previous = null;
            previousLoadedPageCount = 0;
            cursor = null;
            if (!IsCurrentAuthentication(owner, version))
                return false;

            previous = _snapshot.Value;
            previousLoadedPageCount = _loadedPageCount;
            cursor = append ? previous.NextCursor : null;
            if (append && cursor is null)
                return false;

            _snapshot.Value = previous with { IsLoading = true, Error = null };
            return true;
        }
    }

    private async Task<(ImmutableArray<AiJob> Jobs, string? NextCursor, int LoadedPageCount)> LoadJobsAsync(
        string? cursor,
        bool append,
        bool preserveLoadedTail,
        AiJobMonitorSnapshot previous,
        int previousLoadedPageCount,
        CancellationToken token)
    {
        AiJobPage page = await _client.GetPageAsync(new AiJobPageRequest(cursor), token);
        if (preserveLoadedTail)
        {
            return await RefreshLoadedPagesAsync(
                page,
                Math.Max(1, previousLoadedPageCount),
                token);
        }

        return (
            MergeJobs(append ? previous.Jobs : [], page.Jobs),
            page.NextCursor,
            append ? previousLoadedPageCount + 1 : 1);
    }

    // A refresh that finishes after the authentication changed must not publish into the new session.
    private void PublishIfCurrent(
        AuthenticatedUser owner,
        long version,
        AiJobMonitorSnapshot snapshot,
        int? loadedPageCount = null)
    {
        lock (_stateGate)
        {
            if (IsCurrentAuthentication(owner, version))
            {
                if (loadedPageCount is { } pageCount)
                    _loadedPageCount = pageCount;
                _snapshot.Value = snapshot;
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

    private async Task<(ImmutableArray<AiJob> Jobs, string? NextCursor, int PageCount)>
        RefreshLoadedPagesAsync(
            AiJobPage firstPage,
            int pageCount,
            CancellationToken cancellationToken)
    {
        // Re-read exactly the depth the person loaded. A new leading job can push the
        // previous oldest item behind that boundary; retaining every absent old item
        // instead would also retain jobs the server deleted, with no way to distinguish them.
        ImmutableArray<AiJob> jobs = MergeJobs([], firstPage.Jobs);
        string? nextCursor = firstPage.NextCursor;
        int refreshedPages = 1;
        while (refreshedPages < pageCount && nextCursor is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AiJobPage page = await _client.GetPageAsync(
                new AiJobPageRequest(nextCursor),
                cancellationToken);
            jobs = MergeJobs(jobs, page.Jobs);
            nextCursor = page.NextCursor;
            refreshedPages++;
        }

        return (jobs, nextCursor, refreshedPages);
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
            _loadedPageCount = 0;
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
            await RefreshPollingAsync(cancellationToken);
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
                _loadedPageCount = 0;
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
            if (_pollingCts is not null || IsPollingUnneeded_NoLock())
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
            if (IsPollingUnneeded_NoLock())
            {
                cancellationTokenSource = _pollingCts;
            }
        }

        cancellationTokenSource?.Cancel();
    }

    // Callers hold _pollingGate.
    private bool IsPollingUnneeded_NoLock()
        => _pollingLeases == 0 && !GetSnapshot().Jobs.Any(ShouldPoll);

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

    private bool HasPollingLease()
    {
        lock (_pollingGate)
        {
            return _pollingLeases > 0;
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
            if (snapshot.Error is AuthenticationRequiredException)
                continue;

            ImmutableArray<AiJob> pollingJobs = snapshot.Jobs.Where(ShouldPoll).ToImmutableArray();
            foreach (AiJob job in pollingJobs)
            {
                AiJobStatusSemantics status;
                try
                {
                    status = _jobKinds.GetStatus(job);
                }
                catch
                {
                    continue;
                }

                if (!status.ShouldPoll
                    || !_jobKinds.TryAcquireRefreshHandler(
                        job.Kind,
                        out IAiJobRefreshHandlerLease? refreshLease))
                {
                    continue;
                }

                using (refreshLease)
                {
                    try
                    {
                        await refreshLease.Handler.RefreshAsync(job, cancellationToken);
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

            if (pollingJobs.Length > 0 || HasPollingLease() || snapshot.Error is not null)
            {
                await RefreshPollingAsync(cancellationToken);
            }
        }
    }
}
