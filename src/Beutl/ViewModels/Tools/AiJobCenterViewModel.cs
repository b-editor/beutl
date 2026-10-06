using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed partial class AiJobCenterViewModel : IDisposable, IAsyncDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
    private readonly AsyncOperationLifetime _operations = new();
    private readonly object _lifetimeGate = new();
    private readonly ILogger _logger = Log.CreateLogger<AiJobCenterViewModel>();
    private readonly EditViewModel _editViewModel;
    private readonly IAiEntitlementService _entitlements;
    private readonly IAuthenticatedContentService _content;
    private readonly IAiJobClient _jobClient;
    private readonly IAiJobMonitor _jobMonitor;
    private readonly IAiJobKindRegistry _jobKinds;
    private readonly AiJobResultRegistry _resultHandlers;
    private readonly AiJobResultContext _resultContext;
    private readonly ObservableCollection<AiJobItemViewModel> _jobs = [];
    private string? _operationError;
    private string? _snapshotError;
    private AiJobItemViewModel? _confirmationItem;
    private bool _isDisposed;
    private Task? _disposeTask;
    private readonly SemaphoreSlim _previewLoadGate = new(4, 4);
    private readonly HashSet<AiJobItemViewModel> _visiblePreviewItems = [];
    private long _snapshotSequence;
    private long _appliedSnapshotSequence;

    internal AiJobCenterViewModel(
        EditViewModel editViewModel,
        IAiEntitlementService entitlements,
        IAuthenticatedContentService content,
        IAiJobClient jobClient,
        IAiJobMonitor jobMonitor,
        IAiJobKindRegistry jobKinds,
        AiJobResultRegistry resultHandlers,
        Func<AiCaptionHistoryResult, Task<bool>>? openCaptionResult)
    {
        _editViewModel = editViewModel ?? throw new ArgumentNullException(nameof(editViewModel));
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _jobClient = jobClient ?? throw new ArgumentNullException(nameof(jobClient));
        _jobMonitor = jobMonitor ?? throw new ArgumentNullException(nameof(jobMonitor));
        _jobKinds = jobKinds ?? throw new ArgumentNullException(nameof(jobKinds));
        _resultHandlers = resultHandlers ?? throw new ArgumentNullException(nameof(resultHandlers));
        _resultContext = new AiJobResultContext(_editViewModel, _content, openCaptionResult);
        Jobs = new ReadOnlyObservableCollection<AiJobItemViewModel>(_jobs);
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);

        Refresh = new AsyncReactiveCommand(
                IsLoading.CombineLatest(
                    IsAuthenticationRequired,
                    (isLoading, authenticationRequired) => !isLoading && !authenticationRequired))
            .WithSubscribe(() => RefreshJobsAsync(append: false))
            .DisposeWith(_disposables);
        LoadMore = new AsyncReactiveCommand(
                HasMore.CombineLatest(
                    IsLoading,
                    IsAuthenticationRequired,
                    (hasMore, isLoading, authenticationRequired) =>
                        hasMore && !isLoading && !authenticationRequired))
            .WithSubscribe(() => RefreshJobsAsync(append: true))
            .DisposeWith(_disposables);

        _jobMonitor.Snapshot
            .Subscribe(QueueSnapshot)
            .DisposeWith(_disposables);
        _jobMonitor.AcquirePolling().DisposeWith(_disposables);
        _ = RefreshEntitlementsAsync();
    }

    public ReadOnlyObservableCollection<AiJobItemViewModel> Jobs { get; }

    internal AiUsageViewModel Usage { get; }

    internal EditViewModel Editor => _editViewModel;

    public ReactivePropertySlim<bool> IsLoading { get; } = new();

    public ReactivePropertySlim<bool> IsInitialLoading { get; } = new();

    public ReactivePropertySlim<bool> IsListLoading { get; } = new();

    public ReactivePropertySlim<bool> IsAuthenticationRequired { get; } = new();

    public ReactivePropertySlim<bool> HasJobs { get; } = new();

    public ReactivePropertySlim<bool> HasMore { get; } = new();

    public ReactivePropertySlim<bool> ShowEmptyState { get; } = new();

    public ReactivePropertySlim<bool> ShowListFooter { get; } = new();

    public ReactivePropertySlim<string?> Error { get; } = new();

    public ReactivePropertySlim<bool> IsConfirmationOpen { get; } = new();

    public ReactivePropertySlim<bool> IsConfirmationLoading { get; } = new();

    public ReactivePropertySlim<bool> CanConfirm { get; } = new();

    public ReactivePropertySlim<string> ConfirmationTitle { get; } = new(string.Empty);

    public ReactivePropertySlim<string> ConfirmationMessage { get; } = new(string.Empty);

    public ReactivePropertySlim<string> ConfirmationActionText { get; } = new(string.Empty);

    public AsyncReactiveCommand Refresh { get; }

    public AsyncReactiveCommand LoadMore { get; }

    public async Task DeleteJobAsync(AiJobItemViewModel item)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsTerminal)
            return;

        using IDisposable? operation = TryBeginOperation(item);
        if (operation is null)
            return;

        try
        {
            await _jobClient.DeleteAsync(new AiJobId(item.Id), lifetimeOperation.CancellationToken);
            if (!IsDisposed)
            {
                await _jobMonitor.RefreshAsync(lifetimeOperation.CancellationToken);
            }
        }
        catch (AiJobNotFoundException)
        {
            // A different tab or a concurrent refresh already removed it. Re-read the
            // authoritative list instead of presenting an idempotent delete as a failure.
            if (!IsDisposed)
            {
                await _jobMonitor.RefreshAsync(lifetimeOperation.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete AI job {JobId}", item.Id);
            SetOperationError(Strings.AiJobCenter_DeleteFailed);
        }
    }

    public async Task RetryJobAsync(AiJobItemViewModel item)
        => await RetryJobAsync(item, confirmedLease: null, confirmedHandler: null);

    private async Task RetryJobAsync(
        AiJobItemViewModel item,
        IAiJobRetryHandlerLease? confirmedLease,
        IAiJobRetryHandler? confirmedHandler)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
        {
            confirmedLease?.Dispose();
            return;
        }
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry)
        {
            confirmedLease?.Dispose();
            return;
        }

        using IDisposable? operation = TryBeginOperation(item);
        if (operation is null)
        {
            confirmedLease?.Dispose();
            return;
        }

        IAiJobRetryHandlerLease? lease = confirmedLease;

        try
        {
            if (lease is null && !_jobKinds.TryAcquireRetryHandler(item.Job.Kind, out lease))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                return;
            }

            if (!await SubmitRetryAsync(
                    item,
                    lease,
                    confirmed: confirmedLease is not null,
                    confirmedHandler,
                    lifetimeOperation.CancellationToken))
            {
                return;
            }

            item.MarkRetrySubmitted();

            if (!IsDisposed)
            {
                await RefreshAfterRetryAsync(item, lifetimeOperation.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (AiUsageLimitExceededException)
        {
            SetOperationError(Strings.AiUsageLimitExceeded);
        }
        catch (AiJobRetryPreparationRejectedException)
        {
            // The durable key or authenticated account changed after the
            // dialog preflight. Require the user to start a fresh confirmation
            // instead of silently creating a new paid request.
            SetOperationError(Strings.AiResultUnavailable);
        }
        catch (AuthenticationRequiredException)
        {
            SetOperationError(Strings.AiAuthenticationRequired);
        }
        catch (AiJobRetryPreparationUnavailableException)
        {
            SetOperationError(Strings.AiPricingUnavailable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry AI job {JobId}", item.Id);
            SetOperationError(Strings.AiJobCenter_RetryFailed);
        }
    }

    // Sends the retry under the lease, which is disposed once it has gone out or been refused.
    // False when it was refused, with the reason already shown.
    private async Task<bool> SubmitRetryAsync(
        AiJobItemViewModel item,
        IAiJobRetryHandlerLease lease,
        bool confirmed,
        IAiJobRetryHandler? confirmedHandler,
        CancellationToken cancellationToken)
    {
        using (lease)
        {
            AiJobStatusSemantics status = _jobKinds.GetStatus(item.Job);
            IAiJobRetryHandler retryHandler = confirmedHandler ?? lease.Handler;
            if (!retryHandler.CanRetry(item.Job, status))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                return false;
            }

            if (!confirmed)
            {
                AiJobRetryPreflight estimate = await retryHandler.GetPreflightAsync(
                    item.Job,
                    cancellationToken);
                if (!estimate.CanSubmit)
                {
                    SetOperationError(estimate.Explanation);
                    return false;
                }
            }

            AiJobRetryPreparationResult prepared = await retryHandler.PrepareAsync(
                item.Job,
                cancellationToken);
            await using (prepared)
            {
                if (!prepared.IsReady)
                {
                    SetOperationError(prepared.Explanation);
                    return false;
                }

                IAiJobRetryPreparation preparation = prepared.TakePreparation();
                await using (preparation)
                {
                    await preparation.ExecuteAsync(cancellationToken);
                }
            }
        }

        return true;
    }

    // The retry has gone out; a list that fails to refresh is reported but does not undo it.
    private async Task RefreshAfterRetryAsync(AiJobItemViewModel item, CancellationToken cancellationToken)
    {
        try
        {
            await _jobMonitor.RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "AI job {JobId} was retried, but refreshing the job list failed",
                item.Id);
            SetOperationError(Strings.AiJobCenter_LoadFailed);
        }
    }

    internal async Task<AiJobRetryPreflight> GetRetryEstimateAsync(AiJobItemViewModel item)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return RetryUnavailable();
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry)
            return RetryUnavailable();

        try
        {
            if (!_jobKinds.TryAcquireRetryHandler(
                    item.Job.Kind,
                    out IAiJobRetryHandlerLease? lease))
                return RetryUnavailable();

            using (lease)
            {
                AiJobStatusSemantics status = _jobKinds.GetStatus(item.Job);
                IAiJobRetryHandler retryHandler = lease.Handler;
                if (!retryHandler.CanRetry(item.Job, status))
                {
                    return RetryUnavailable();
                }

                AiJobRetryPreflight estimate = await retryHandler.GetPreflightAsync(
                    item.Job,
                    lifetimeOperation.CancellationToken);
                SetOperationError(estimate.CanSubmit ? null : estimate.Explanation);
                return estimate;
            }
        }
        catch (AuthenticationRequiredException)
        {
            SetOperationError(Strings.AiAuthenticationRequired);
            return new AiJobRetryPreflight(false, false, Strings.AiAuthenticationRequired);
        }
        catch (AiJobRetryPreparationRejectedException)
        {
            SetOperationError(Strings.AiResultUnavailable);
            return new AiJobRetryPreflight(false, false, Strings.AiResultUnavailable);
        }
        catch (AiJobRetryPreparationUnavailableException)
        {
            SetOperationError(Strings.AiPricingUnavailable);
            return RetryUnavailable();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            return RetryUnavailable();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh authoritative pricing before retrying AI job {JobId}", item.Id);
            SetOperationError(Strings.AiPricingUnavailable);
            return RetryUnavailable();
        }
    }

    public async Task AddToSceneAsync(AiJobItemViewModel item)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanAddToScene)
            return;

        using IDisposable? operation = TryBeginOperation(item);
        if (operation is null)
            return;

        try
        {
            AiJobStatusSemantics status = _jobKinds.GetStatus(item.Job);
            if (!_resultHandlers.TryAcquireApplicator(
                    item.Job.Kind,
                    out IAiJobResultApplicatorLease? applicatorLease))
            {
                return;
            }

            using (applicatorLease)
            {
                IAiJobResultApplicator applicator = applicatorLease.Applicator;
                if (!applicator.CanApply(item.Job, status))
                    return;

                await applicator.ApplyAsync(
                    item.Job,
                    _resultContext,
                    lifetimeOperation.CancellationToken);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add AI job {JobId} result to the scene", item.Id);
            SetOperationError(Strings.AiJobCenter_AddFailed);
        }
    }

    public void Dispose()
    {
        _ = DisposeAsync().AsTask().ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public ValueTask DisposeAsync()
    {
        lock (_lifetimeGate)
        {
            if (_disposeTask is not null)
                return new ValueTask(_disposeTask);

            _isDisposed = true;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            _ = CompleteDisposeAsync(completion);
            return new ValueTask(completion.Task);
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        CancelConfirmation();
        try
        {
            await _operations.DisposeAsync(
            cancelAdditionalWork: () =>
            {
                try { _lifetimeCts.Cancel(); }
                catch (Exception ex) { _logger.LogDebug(ex, "AI job center lifetime cancellation callback failed"); }
            },
            disposeResources: () =>
            {
                _disposables.Dispose();
                foreach (AiJobItemViewModel job in _jobs) job.Dispose();
                _jobs.Clear();
                _visiblePreviewItems.Clear();
                IsLoading.Dispose();
                IsInitialLoading.Dispose();
                IsListLoading.Dispose();
                IsAuthenticationRequired.Dispose();
                HasJobs.Dispose();
                HasMore.Dispose();
                ShowEmptyState.Dispose();
                ShowListFooter.Dispose();
                Error.Dispose();
                IsConfirmationOpen.Dispose();
                IsConfirmationLoading.Dispose();
                CanConfirm.Dispose();
                ConfirmationTitle.Dispose();
                ConfirmationMessage.Dispose();
                ConfirmationActionText.Dispose();
                _previewLoadGate.Dispose();
                _lifetimeCts.Dispose();
                return ValueTask.CompletedTask;
            });
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private bool IsDisposed => Volatile.Read(ref _isDisposed);

    private async Task RefreshJobsAsync(bool append)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        SetOperationError(null);
        if (append)
        {
            await _jobMonitor.LoadNextPageAsync(lifetimeOperation.CancellationToken);
        }
        else
        {
            await _jobMonitor.RefreshAsync(lifetimeOperation.CancellationToken);
        }
    }

    private async Task RefreshEntitlementsAsync()
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(lifetimeOperation.CancellationToken);
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI entitlement refresh failed while opening the job center");
        }
    }

    private void QueueSnapshot(AiJobMonitorSnapshot snapshot)
    {
        if (IsDisposed)
            return;

        long sequence = Interlocked.Increment(ref _snapshotSequence);

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySnapshot(snapshot, sequence);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot, sequence));
        }
    }

    internal void ApplySnapshot(AiJobMonitorSnapshot snapshot)
        => ApplySnapshot(snapshot, Interlocked.Increment(ref _snapshotSequence));

    internal void ApplySnapshot(AiJobMonitorSnapshot snapshot, long sequence)
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed || sequence < _appliedSnapshotSequence)
                return;
            _appliedSnapshotSequence = sequence;

            var existing = _jobs.ToDictionary(item => item.Id, StringComparer.Ordinal);
            var desired = new List<AiJobItemViewModel>(snapshot.Jobs.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (AiJob response in snapshot.Jobs)
            {
                string id = response.Id.Value;
                if (!seen.Add(id))
                    continue;

                if (existing.TryGetValue(id, out AiJobItemViewModel? item))
                {
                    item.Update(response);
                }
                else
                {
                    item = new AiJobItemViewModel(response, _jobKinds, _resultHandlers);
                }

                desired.Add(item);
            }

            SynchronizeJobs(desired);
            _visiblePreviewItems.RemoveWhere(item => !_jobs.Contains(item));
            if (_confirmationItem is not null && !_jobs.Contains(_confirmationItem))
            {
                CancelConfirmation();
            }

            bool hasJobs = _jobs.Count > 0;
            bool authenticationRequired = snapshot.Error is AuthenticationRequiredException;
            if (authenticationRequired != IsAuthenticationRequired.Value)
            {
                _operationError = null;
            }

            IsAuthenticationRequired.Value = authenticationRequired;
            IsLoading.Value = snapshot.IsLoading;
            IsInitialLoading.Value = snapshot.IsLoading && !hasJobs;
            IsListLoading.Value = snapshot.IsLoading && hasJobs;
            HasJobs.Value = hasJobs;
            HasMore.Value = snapshot.NextCursor is not null;
            ShowEmptyState.Value = !snapshot.IsLoading && !hasJobs && snapshot.Error is null;
            ShowListFooter.Value = hasJobs && (snapshot.IsLoading || snapshot.NextCursor is not null);
            _snapshotError = snapshot.Error switch
            {
                null => null,
                AuthenticationRequiredException => Strings.AiAuthenticationRequired,
                _ => Strings.AiJobCenter_LoadFailed,
            };
            UpdateVisibleError();
            LoadVisiblePreviews_NoLock();
        }
    }

    private void SynchronizeJobs(IReadOnlyList<AiJobItemViewModel> desired)
    {
        for (int index = 0; index < desired.Count; index++)
        {
            AiJobItemViewModel item = desired[index];
            if (index < _jobs.Count && ReferenceEquals(_jobs[index], item))
                continue;

            int oldIndex = _jobs.IndexOf(item);
            if (oldIndex >= 0)
            {
                _jobs.Move(oldIndex, index);
            }
            else
            {
                _jobs.Insert(index, item);
            }
        }

        while (_jobs.Count > desired.Count)
        {
            AiJobItemViewModel stale = _jobs[^1];
            _jobs.RemoveAt(_jobs.Count - 1);
            stale.Dispose();
        }
    }

    private IDisposable? TryBeginOperation(AiJobItemViewModel item)
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
                return null;

            IDisposable? operation = item.TryBeginOperation();
            if (operation is not null)
            {
                _operationError = null;
                UpdateVisibleError();
            }
            return operation;
        }
    }

    private void SetOperationError(string? error)
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
                return;

            _operationError = error;
            UpdateVisibleError();
        }
    }

    private void UpdateVisibleError()
    {
        Error.Value = IsAuthenticationRequired.Value
            ? Strings.AiAuthenticationRequired
            : _operationError ?? _snapshotError;
    }

    private static AiJobRetryPreflight RetryUnavailable()
        => new(false, false, Strings.AiPricingUnavailable);

}
