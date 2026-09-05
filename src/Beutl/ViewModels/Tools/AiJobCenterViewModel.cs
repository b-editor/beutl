using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.Editor.Services.Captions;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using SkiaSharp;

namespace Beutl.ViewModels.Tools;

public sealed class AiJobCenterViewModel : IDisposable, IAsyncDisposable
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
    private readonly AiJobResultHandlerRegistry _resultHandlers;
    private readonly AiJobResultContext _resultContext;
    private readonly ObservableCollection<AiJobItemViewModel> _jobs = [];
    private string? _operationError;
    private string? _snapshotError;
    private AiJobConfirmationAction _confirmationAction;
    private AiJobItemViewModel? _confirmationItem;
    private IAiJobKindLease? _confirmationLease;
    private IAiJobRetryHandler? _confirmationHandler;
    private Task<AiJobRetryPreflight>? _confirmationPreflightTask;
    private CancellationTokenSource? _confirmationPreflightCts;
    private long _confirmationRevision;
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
        AiJobResultHandlerRegistry resultHandlers,
        Action<AiCaptionHistoryResult>? openCaptionResult)
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

    internal async Task RequestRetryConfirmationAsync(AiJobItemViewModel item)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry || IsDisposed)
            return;

        ReleaseConfirmationResources();
        long revision = Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.Retry;
        _confirmationItem = item;
        _confirmationLease = null;
        _confirmationHandler = null;
        ConfirmationTitle.Value = Strings.AiJobCenter_RetryTitle;
        ConfirmationMessage.Value = Strings.AiJobCenter_CheckingRetryCost;
        ConfirmationActionText.Value = Strings.AiJobCenter_Retry;
        CanConfirm.Value = false;
        IsConfirmationLoading.Value = true;
        IsConfirmationOpen.Value = true;

        IAiJobKindLease? lease = null;
        Task<AiJobRetryPreflight>? preflightTask = null;
        CancellationTokenSource? preflightCts = null;
        try
        {
            if (!_jobKinds.TryAcquire(item.Job.Kind, out lease))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                return;
            }

            _confirmationLease = lease;

            AiJobKindDescriptor descriptor = lease.Descriptor;
            AiJobStatusSemantics status = descriptor.StatusResolver.Resolve(item.Job.Status);
            if (descriptor.RetryHandler is not { } retryHandler
                || !retryHandler.CanRetry(item.Job, status))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                ReleaseConfirmationResources();
                return;
            }

            _confirmationHandler = retryHandler;
            preflightCts = CancellationTokenSource.CreateLinkedTokenSource(
                lifetimeOperation.CancellationToken);
            _confirmationPreflightCts = preflightCts;
            preflightTask = retryHandler.GetPreflightAsync(
                item.Job,
                preflightCts.Token).AsTask();
            _confirmationPreflightTask = preflightTask;
            AiJobRetryPreflight estimate = await preflightTask;
            if (ReferenceEquals(_confirmationPreflightTask, preflightTask))
            {
                _confirmationPreflightTask = null;
                if (ReferenceEquals(_confirmationPreflightCts, preflightCts))
                {
                    _confirmationPreflightCts = null;
                    preflightCts.Dispose();
                }
            }
            if (!TryPublishConfirmationPreflight(revision, lease, estimate))
                return;
            if (!estimate.CanSubmit)
            {
                ReleaseConfirmationResources();
                lease = null;
            }
        }
        catch (AuthenticationRequiredException)
        {
            if (IsCurrentConfirmation(revision, lease))
            {
                SetOperationError(Strings.AiAuthenticationRequired);
                ReleaseConfirmationResources();
            }
        }
        catch (AiJobRetryPreparationRejectedException)
        {
            if (IsCurrentConfirmation(revision, lease))
            {
                SetOperationError(Strings.AiResultUnavailable);
                ReleaseConfirmationResources();
            }
        }
        catch (AiJobRetryPreparationUnavailableException)
        {
            if (IsCurrentConfirmation(revision, lease))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                ReleaseConfirmationResources();
            }
        }
        catch (OperationCanceledException) when (preflightCts?.IsCancellationRequested == true)
        {
            if (IsCurrentConfirmation(revision, lease))
                ReleaseConfirmationResources();
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
            if (IsCurrentConfirmation(revision, lease))
                ReleaseConfirmationResources();
        }
        catch (Exception ex)
        {
            if (IsCurrentConfirmation(revision, lease))
            {
                _logger.LogDebug(ex, "Failed to refresh authoritative pricing before retrying AI job {JobId}", item.Id);
                SetOperationError(Strings.AiPricingUnavailable);
                ReleaseConfirmationResources();
            }
        }
        finally
        {
            CompleteConfirmationLoading(revision);
        }
    }

    private bool TryPublishConfirmationPreflight(
        long revision,
        IAiJobKindLease lease,
        AiJobRetryPreflight estimate)
    {
        lock (_lifetimeGate)
        {
            if (revision != _confirmationRevision
                || _isDisposed
                || !ReferenceEquals(_confirmationLease, lease))
                return false;

            ConfirmationMessage.Value = estimate.IsAvailable
                ? string.Join(
                    Environment.NewLine,
                    Strings.AiJobCenter_RetryConfirmation,
                    estimate.Explanation)
                : estimate.Explanation;
            CanConfirm.Value = estimate.CanSubmit;
            return true;
        }
    }

    private void CompleteConfirmationLoading(long revision)
    {
        lock (_lifetimeGate)
        {
            if (revision == _confirmationRevision && !_isDisposed)
                IsConfirmationLoading.Value = false;
        }
    }

    private bool IsCurrentConfirmation(long revision, IAiJobKindLease? lease)
        => revision == Volatile.Read(ref _confirmationRevision)
            && !IsDisposed
            && (lease is null || ReferenceEquals(_confirmationLease, lease));

    internal void RequestDeleteConfirmation(AiJobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanDelete || IsDisposed)
            return;

        ReleaseConfirmationResources();
        Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.Delete;
        _confirmationItem = item;
        ConfirmationTitle.Value = string.IsNullOrWhiteSpace(item.Summary)
            ? Strings.AiJobCenter_DeleteTitle
            : string.Format(Strings.AiJobCenter_DeleteTitleFormat, Shorten(item.Summary));
        ConfirmationMessage.Value = Strings.AiJobCenter_DeleteConfirmation;
        ConfirmationActionText.Value = Strings.Delete;
        CanConfirm.Value = true;
        IsConfirmationLoading.Value = false;
        IsConfirmationOpen.Value = true;
    }

    private static string Shorten(string text, int maximumLength = 48)
    {
        string normalized = text.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..maximumLength].TrimEnd() + "…";
    }

    private void ReleaseConfirmationResources()
    {
        IAiJobKindLease? lease = Interlocked.Exchange(ref _confirmationLease, null);
        Task<AiJobRetryPreflight>? preflight = Interlocked.Exchange(ref _confirmationPreflightTask, null);
        CancellationTokenSource? preflightCts = Interlocked.Exchange(
            ref _confirmationPreflightCts,
            null);
        _confirmationHandler = null;
        CancelPreflight(preflightCts);
        if (lease is not null)
        {
            if (preflight is { IsCompleted: false })
            {
                _ = DisposeLeaseAfterPreflightAsync(preflight, lease, preflightCts);
            }
            else
            {
                DisposeConfirmationLease(lease);
                preflightCts?.Dispose();
            }
        }
        else
        {
            preflightCts?.Dispose();
        }
    }

    private async Task DisposeLeaseAfterPreflightAsync(
        Task<AiJobRetryPreflight> preflight,
        IAiJobKindLease lease,
        CancellationTokenSource? preflightCts)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        try
        {
            await preflight;
        }
        catch
        {
        }
        DisposeConfirmationLease(lease);
        preflightCts?.Dispose();
    }

    private void DisposeConfirmationLease(IAiJobKindLease lease)
    {
        try
        {
            lease.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to release an AI retry confirmation lease");
        }
    }

    private void CancelPreflight(CancellationTokenSource? preflightCts)
    {
        if (preflightCts is null)
            return;

        try
        {
            preflightCts.Cancel();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AI retry preflight cancellation callback failed");
        }
    }

    private void ClearConfirmationState()
    {
        Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.None;
        _confirmationItem = null;
        IsConfirmationOpen.Value = false;
        IsConfirmationLoading.Value = false;
        CanConfirm.Value = false;
    }

    internal void CancelConfirmation()
    {
        ClearConfirmationState();
        ReleaseConfirmationResources();
    }

    internal async Task ConfirmPendingActionAsync()
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;
        if (!CanConfirm.Value || _confirmationItem is not { } item)
            return;

        AiJobConfirmationAction action = _confirmationAction;
        IAiJobKindLease? lease = Interlocked.Exchange(ref _confirmationLease, null);
        IAiJobRetryHandler? handler = _confirmationHandler;
        _confirmationHandler = null;
        ClearConfirmationState();
        switch (action)
        {
            case AiJobConfirmationAction.Retry:
                await RetryJobAsync(item, lease, handler);
                break;
            case AiJobConfirmationAction.Delete:
                lease?.Dispose();
                await DeleteJobAsync(item);
                break;
            default:
                lease?.Dispose();
                break;
        }
    }

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
        IAiJobKindLease? confirmedLease,
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

        IAiJobKindLease? lease = confirmedLease;

        try
        {
            if (lease is null && !_jobKinds.TryAcquire(item.Job.Kind, out lease))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                return;
            }

            using (lease)
            {
                AiJobKindDescriptor descriptor = lease.Descriptor;
                AiJobStatusSemantics status = descriptor.StatusResolver.Resolve(item.Job.Status);
                IAiJobRetryHandler? retryHandler = confirmedHandler ?? descriptor.RetryHandler;
                if (retryHandler is null
                    || !retryHandler.CanRetry(item.Job, status))
                {
                    SetOperationError(Strings.AiPricingUnavailable);
                    return;
                }

                if (confirmedLease is null)
                {
                    AiJobRetryPreflight estimate = await retryHandler.GetPreflightAsync(
                        item.Job,
                        lifetimeOperation.CancellationToken);
                    if (!estimate.CanSubmit)
                    {
                        SetOperationError(estimate.Explanation);
                        return;
                    }
                }

                AiJobRetryPreparationResult prepared = await retryHandler.PrepareAsync(
                    item.Job,
                    lifetimeOperation.CancellationToken);
                await using (prepared)
                {
                    if (!prepared.IsReady)
                    {
                        SetOperationError(prepared.Explanation);
                        return;
                    }

                    IAiJobRetryPreparation preparation = prepared.TakePreparation();
                    await using (preparation)
                    {
                        await preparation.ExecuteAsync(lifetimeOperation.CancellationToken);
                    }
                }
            }

            if (!IsDisposed)
            {
                await _jobMonitor.RefreshAsync(lifetimeOperation.CancellationToken);
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
            if (!_jobKinds.TryAcquire(item.Job.Kind, out IAiJobKindLease? lease))
                return RetryUnavailable();

            using (lease)
            {
                AiJobKindDescriptor descriptor = lease.Descriptor;
                AiJobStatusSemantics status = descriptor.StatusResolver.Resolve(item.Job.Status);
                if (descriptor.RetryHandler is not { } retryHandler
                    || !retryHandler.CanRetry(item.Job, status))
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
            AiJobStatusSemantics status = AiJobStatusSemantics.Unknown;
            IAiJobKindLease? kindLease = null;
            if (_jobKinds.TryAcquire(item.Job.Kind, out kindLease))
            {
                status = kindLease.Descriptor.StatusResolver.Resolve(item.Job.Status);
            }

            using (kindLease)
            {
                if (!_resultHandlers.TryAcquire(item.Job.Kind, out IAiJobResultHandlerLease? resultHandlerLease))
                {
                    return;
                }

                using (resultHandlerLease)
                {
                    IAiJobResultHandler resultHandler = resultHandlerLease.Handler;
                    if (!resultHandler.CanHandle(item.Job, status))
                        return;

                    await resultHandler.HandleAsync(item.Job, _resultContext, lifetimeOperation.CancellationToken);
                }
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

    internal void SetPreviewVisibility(AiJobItemViewModel item, bool isVisible)
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
                return;
            if (!isVisible || !_jobs.Contains(item))
            {
                _visiblePreviewItems.Remove(item);
                return;
            }

            _visiblePreviewItems.Add(item);
            TryLoadPreview_NoLock(item);
        }
    }

    private void LoadVisiblePreviews_NoLock()
    {
        foreach (AiJobItemViewModel item in _visiblePreviewItems)
            TryLoadPreview_NoLock(item);
    }

    private void TryLoadPreview_NoLock(AiJobItemViewModel item)
    {
        if (item.TryClaimPreviewLoad())
            _ = LoadPreviewAsync(item);
    }

    private async Task LoadPreviewAsync(AiJobItemViewModel item)
    {
        using AsyncOperationLifetime.Operation? lifetimeOperation = _operations.TryEnter();
        if (lifetimeOperation is null)
            return;

        bool enteredGate = false;
        try
        {
            await _previewLoadGate.WaitAsync(lifetimeOperation.CancellationToken);
            enteredGate = true;
            lock (_lifetimeGate)
            {
                if (_isDisposed || !_visiblePreviewItems.Contains(item))
                {
                    item.ResetPreviewLoadClaim();
                    return;
                }
            }
            if (item.ContentUri is not { } contentUri)
            {
                item.ResetPreviewLoadClaim();
                return;
            }

            using var buffer = new SizeLimitedMemoryStream(
                checked((int)AiRequestLimits.MaxImageUploadBytes));
            await _content.CopyToAsync(contentUri, buffer, lifetimeOperation.CancellationToken);
            buffer.Position = 0;
            Bitmap preview = await Task.Run(
                () => DecodePreview(buffer),
                lifetimeOperation.CancellationToken);
            item.SetPreview(Ref<Bitmap>.Create(preview));
        }
        catch (OperationCanceledException) when (lifetimeOperation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // The card still names the job; only the picture beside it is missing.
            _logger.LogDebug(ex, "Failed to load a preview for AI job {JobId}", item.Id);
            item.ResetPreviewLoadClaim();
        }
        finally
        {
            if (enteredGate)
                _previewLoadGate.Release();
        }
    }

    internal static Bitmap DecodePreview(Stream encodedContent)
    {
        ArgumentNullException.ThrowIfNull(encodedContent);
        using SKCodec codec = SKCodec.Create(encodedContent)
            ?? throw new InvalidDataException("Failed to inspect the AI preview image.");
        SKImageInfo sourceInfo = codec.Info;
        if (sourceInfo.Width <= 0
            || sourceInfo.Height <= 0
            || sourceInfo.Width > 8_192
            || sourceInfo.Height > 8_192
            || (long)sourceInfo.Width * sourceInfo.Height > 16_777_216)
        {
            throw new InvalidDataException("The AI preview image dimensions are unsupported.");
        }

        const int maxDimension = 512;
        double scale = Math.Min(1d, Math.Min(
            maxDimension / (double)sourceInfo.Width,
            maxDimension / (double)sourceInfo.Height));
        int width = Math.Max(1, (int)Math.Round(sourceInfo.Width * scale));
        int height = Math.Max(1, (int)Math.Round(sourceInfo.Height * scale));
        var decodeInfo = new SKImageInfo(
            sourceInfo.Width,
            sourceInfo.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        if (codec.StartScanlineDecode(decodeInfo) != SKCodecResult.Success)
        {
            // Some codecs, notably interlaced PNG, cannot expose scanlines. Keep
            // their fallback surface far below the validated server maximum so
            // four concurrent previews still have a deterministic memory bound.
            if ((long)sourceInfo.Width * sourceInfo.Height > 4_194_304)
            {
                throw new InvalidDataException(
                    "The AI preview image is too large for its decoder.");
            }

            using SKBitmap source = SKBitmap.Decode(codec)
                ?? throw new InvalidDataException("Failed to decode the AI preview image.");
            using SKBitmap resized = source.Resize(
                new SKImageInfo(width, height),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
                ?? throw new InvalidDataException("Failed to resize the AI preview image.");
            return EncodePreview(resized);
        }

        int sourceRowBytes = checked(sourceInfo.Width * 4);
        byte[] sourceRow = new byte[sourceRowBytes];
        byte[] thumbnailPixels = new byte[checked(width * height * 4)];
        GCHandle rowHandle = GCHandle.Alloc(sourceRow, GCHandleType.Pinned);
        try
        {
            int nextSourceRow = 0;
            for (int y = 0; y < height; y++)
            {
                int sourceY = Math.Min(
                    sourceInfo.Height - 1,
                    (int)(((long)(2 * y + 1) * sourceInfo.Height) / (2L * height)));
                int rowsToSkip = sourceY - nextSourceRow;
                if (rowsToSkip > 0 && !codec.SkipScanlines(rowsToSkip))
                    throw new InvalidDataException("Failed to seek within the AI preview image.");
                if (codec.GetScanlines(rowHandle.AddrOfPinnedObject(), 1, sourceRowBytes) != 1)
                    throw new InvalidDataException("Failed to decode the AI preview image.");
                nextSourceRow = sourceY + 1;

                int targetRow = y * width * 4;
                for (int x = 0; x < width; x++)
                {
                    int sourceX = Math.Min(
                        sourceInfo.Width - 1,
                        (int)(((long)(2 * x + 1) * sourceInfo.Width) / (2L * width)));
                    Buffer.BlockCopy(sourceRow, sourceX * 4, thumbnailPixels, targetRow + x * 4, 4);
                }
            }
        }
        finally
        {
            rowHandle.Free();
        }

        using var thumbnail = new SKBitmap(new SKImageInfo(
            width,
            height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul));
        Marshal.Copy(thumbnailPixels, 0, thumbnail.GetPixels(), thumbnailPixels.Length);
        return EncodePreview(thumbnail);
    }

    private static Bitmap EncodePreview(SKBitmap thumbnail)
    {
        using SKImage image = SKImage.FromBitmap(thumbnail);
        using SKData png = image.Encode(SKEncodedImageFormat.Png, 90);
        using var stream = new MemoryStream();
        png.SaveTo(stream);
        stream.Position = 0;
        return Bitmap.FromStream(stream);
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

internal enum AiJobConfirmationAction
{
    None,
    Retry,
    Delete,
}

public sealed class AiJobItemViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly object _stateGate = new();
    private readonly IAiJobKindRegistry _jobKinds;
    private readonly AiJobResultHandlerRegistry _resultHandlers;
    private AiJob _response;
    private Ref<Bitmap>? _preview;
    private bool _disposeRequested;
    private bool _isOperationActive;
    private bool _isBusyDisposed;
    private bool _previewRequested;

    public AiJobItemViewModel(
        AiJob response,
        IAiJobKindRegistry jobKinds,
        AiJobResultHandlerRegistry resultHandlers)
    {
        ArgumentNullException.ThrowIfNull(response);
        _jobKinds = jobKinds ?? throw new ArgumentNullException(nameof(jobKinds));
        _resultHandlers = resultHandlers ?? throw new ArgumentNullException(nameof(resultHandlers));
        Id = response.Id.Value;
        _response = response;
        ApplyResponse();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Kind { get; private set; } = string.Empty;

    public string Status { get; private set; } = string.Empty;

    public Uri? ContentUri { get; private set; }

    public string? FileId { get; private set; }

    public string? Error { get; private set; }

    public bool CanRetry { get; private set; }

    public string? Prompt { get; private set; }

    public string? ImageSize { get; private set; }

    public string? Resolution { get; private set; }

    public string? Task { get; private set; }

    public string? Language { get; private set; }

    public int? DurationSeconds { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public string Details { get; private set; } = string.Empty;

    public bool HasDetails { get; private set; }

    public string CreatedAtText { get; private set; } = string.Empty;

    public string CreatedAtTooltip { get; private set; } = string.Empty;

    /// <summary>
    /// Whether this job's result is a picture worth showing beside its prompt.
    /// </summary>
    public bool HasImagePreview { get; private set; }

    public Ref<Bitmap>? Preview
    {
        get
        {
            lock (_stateGate)
                return _preview;
        }
    }

    public bool IsTerminal { get; private set; }

    public bool ShouldPoll { get; private set; }

    public bool IsFailed { get; private set; }

    public bool CanDelete { get; private set; }

    public bool CanAddToScene { get; private set; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public string KindDisplayName { get; private set; } = string.Empty;

    public string StatusDisplayName { get; private set; } = string.Empty;

    internal AiJob Job => _response;

    /// <summary>
    /// Claims the one download this item's preview is allowed, so a list that
    /// refreshes while polling does not fetch the same picture again.
    /// </summary>
    internal bool TryClaimPreviewLoad()
    {
        lock (_stateGate)
        {
            if (_previewRequested || !HasImagePreview || _disposeRequested)
                return false;

            _previewRequested = true;
            return true;
        }
    }

    internal bool IsPreviewLoadRequested
    {
        get
        {
            lock (_stateGate)
                return _previewRequested;
        }
    }

    internal void ResetPreviewLoadClaim()
    {
        lock (_stateGate)
        {
            if (!_disposeRequested)
                _previewRequested = false;
        }
    }

    internal void SetPreview(Ref<Bitmap>? preview)
    {
        Ref<Bitmap>? previous;
        lock (_stateGate)
        {
            if (_disposeRequested)
            {
                preview?.Dispose();
                return;
            }

            if (ReferenceEquals(_preview, preview))
                return;

            previous = _preview;
            _preview = preview;
            previous?.Dispose();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        }
    }

    internal void Update(AiJob response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (!string.Equals(Id, response.Id.Value, StringComparison.Ordinal))
            throw new ArgumentException("The updated AI job must have the same id.", nameof(response));

        lock (_stateGate)
        {
            if (_disposeRequested)
                return;

            _response = response;
            ApplyResponse();
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    internal IDisposable? TryBeginOperation()
    {
        lock (_stateGate)
        {
            if (_disposeRequested || _isOperationActive)
                return null;

            _isOperationActive = true;
            IsBusy.Value = true;
            return Disposable.Create(EndOperation);
        }
    }

    public void Dispose()
    {
        Ref<Bitmap>? previous;
        lock (_stateGate)
        {
            if (_disposeRequested)
                return;

            _disposeRequested = true;
            if (!_isOperationActive)
            {
                DisposeBusyProperty();
            }

            previous = _preview;
            _preview = null;
            previous?.Dispose();
        }

    }

    private void EndOperation()
    {
        lock (_stateGate)
        {
            if (!_isOperationActive)
                return;

            _isOperationActive = false;
            IsBusy.Value = false;
            if (_disposeRequested)
            {
                DisposeBusyProperty();
            }
        }
    }

    private void DisposeBusyProperty()
    {
        if (_isBusyDisposed)
            return;

        _isBusyDisposed = true;
        IsBusy.Dispose();
    }

    private void ApplyResponse()
    {
        Kind = NormalizeToken(_response.Kind.Value);
        Status = NormalizeToken(_response.Status.Value);
        ContentUri = _response.ContentUri;
        FileId = _response.FileId?.Value;
        Error = AiErrorMessage.Localize(_response.Error);
        Prompt = GetString("prompt");
        ImageSize = GetString("size");
        Resolution = GetString("resolution");
        Task = GetString("task");
        Language = GetString("targetLanguage") ?? GetString("language");
        DurationSeconds = GetInt32("durationSeconds");
        GeneratedAt = _response.CreatedAt.ToUniversalTime();
        var presentation = new AiJobPresentation(
            Kind.Length > 0 ? Kind : Strings.AiJobCenter_NoDescription,
            Status.Length > 0 ? Status : Strings.AiJobCenter_NoDescription,
            Prompt
            ?? GetString("filename")
            ?? Strings.AiJobCenter_NoDescription,
            CreateDetails(),
            false);
        AiJobStatusSemantics status = AiJobStatusSemantics.Unknown;
        bool canRetry = false;
        bool canHandleResult = false;
        if (_jobKinds.TryAcquire(_response.Kind, out IAiJobKindLease? lease))
        {
            using (lease)
            {
                AiJobKindDescriptor descriptor = lease.Descriptor;
                try
                {
                    status = descriptor.StatusResolver.Resolve(_response.Status);
                }
                catch
                {
                    status = AiJobStatusSemantics.Unknown;
                }
                canRetry = descriptor.RetryHandler?.CanRetry(_response, status) == true;
            }
        }

        if (_resultHandlers.TryAcquire(_response.Kind, out IAiJobResultHandlerLease? resultHandlerLease))
        {
            using (resultHandlerLease)
            {
                IAiJobResultHandler resultHandler = resultHandlerLease.Handler;
                try
                {
                    presentation = resultHandler.Present(_response, status)
                        ?? throw new InvalidOperationException(
                            $"AI job result handler for '{_response.Kind}' returned no presentation.");
                    canHandleResult = resultHandler.CanHandle(_response, status);
                }
                catch
                {
                    canHandleResult = false;
                }
            }
        }

        Summary = presentation.Summary;
        Details = presentation.Details;
        HasDetails = Details.Length > 0;
        CreatedAtText = RelativeTimeText.Format(_response.CreatedAt, DateTimeOffset.Now);
        CreatedAtTooltip = _response.CreatedAt.ToLocalTime().ToString("g");
        IsTerminal = status.IsTerminal;
        ShouldPoll = status.ShouldPoll;
        IsFailed = presentation.IsFailure;
        CanDelete = status.IsTerminal;
        CanRetry = canRetry;
        CanAddToScene = canHandleResult;
        KindDisplayName = presentation.KindDisplayName;
        StatusDisplayName = presentation.StatusDisplayName;
        HasImagePreview = presentation.HasImagePreview && ContentUri is not null && canHandleResult;
    }

    private string CreateDetails()
    {
        var details = new List<string>();
        if (ImageSize is not null)
        {
            details.Add(ImageSize);
        }
        if (DurationSeconds is not null)
        {
            details.Add($"{DurationSeconds} {Strings.AiVideoSeconds}");
        }
        if (Resolution is not null)
        {
            details.Add(Resolution);
        }
        if (Language is not null)
        {
            details.Add(Language);
        }
        return string.Join(" · ", details);
    }

    private string? GetString(string propertyName)
    {
        if (_response.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NormalizeText(value.GetString());
    }

    private int? GetInt32(string propertyName)
    {
        if (_response.InputParameters is not { ValueKind: JsonValueKind.Object } input
            || !input.TryGetProperty(propertyName, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out int result))
        {
            return null;
        }

        return result;
    }

    private static string NormalizeToken(string? value)
        => NormalizeText(value)?.ToLowerInvariant() ?? string.Empty;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
