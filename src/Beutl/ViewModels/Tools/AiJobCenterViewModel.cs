using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class AiJobCenterViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly LifetimeCancellationSource _lifetimeCts = new();
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
    private long _confirmationRevision;
    private bool _isDisposed;

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

    public ToolTabExtension Extension => AiJobCenterTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.AiJobCenter);

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
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry || IsDisposed)
            return;

        long revision = Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.Retry;
        _confirmationItem = item;
        ConfirmationTitle.Value = Strings.AiJobCenter_RetryTitle;
        ConfirmationMessage.Value = Strings.AiJobCenter_CheckingRetryCost;
        ConfirmationActionText.Value = Strings.AiJobCenter_Retry;
        CanConfirm.Value = false;
        IsConfirmationLoading.Value = true;
        IsConfirmationOpen.Value = true;

        AiJobRetryPreflight estimate = await GetRetryEstimateAsync(item);
        if (revision != Volatile.Read(ref _confirmationRevision) || IsDisposed)
            return;

        ConfirmationMessage.Value = estimate.IsAvailable
            ? string.Join(
                Environment.NewLine,
                Strings.AiJobCenter_RetryConfirmation,
                estimate.Explanation)
            : estimate.Explanation;
        CanConfirm.Value = estimate.CanSubmit;
        IsConfirmationLoading.Value = false;
    }

    internal void RequestDeleteConfirmation(AiJobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanDelete || IsDisposed)
            return;

        Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.Delete;
        _confirmationItem = item;
        ConfirmationTitle.Value = Strings.AiJobCenter_DeleteTitle;
        ConfirmationMessage.Value = Strings.AiJobCenter_DeleteConfirmation;
        ConfirmationActionText.Value = Strings.Delete;
        CanConfirm.Value = true;
        IsConfirmationLoading.Value = false;
        IsConfirmationOpen.Value = true;
    }

    internal void CancelConfirmation()
    {
        Interlocked.Increment(ref _confirmationRevision);
        _confirmationAction = AiJobConfirmationAction.None;
        _confirmationItem = null;
        IsConfirmationOpen.Value = false;
        IsConfirmationLoading.Value = false;
        CanConfirm.Value = false;
    }

    internal async Task ConfirmPendingActionAsync()
    {
        if (!CanConfirm.Value || _confirmationItem is not { } item)
            return;

        AiJobConfirmationAction action = _confirmationAction;
        CancelConfirmation();
        switch (action)
        {
            case AiJobConfirmationAction.Retry:
                await RetryJobAsync(item);
                break;
            case AiJobConfirmationAction.Delete:
                await DeleteJobAsync(item);
                break;
        }
    }

    public async Task DeleteJobAsync(AiJobItemViewModel item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.IsTerminal)
            return;

        using IDisposable? operation = TryBeginOperation(item);
        if (operation is null)
            return;

        try
        {
            await _jobClient.DeleteAsync(new AiJobId(item.Id), _lifetimeCts.Token);
            if (!IsDisposed)
            {
                await _jobMonitor.RefreshAsync(_lifetimeCts.Token);
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
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanRetry)
            return;

        using IDisposable? operation = TryBeginOperation(item);
        if (operation is null)
            return;

        try
        {
            if (!_jobKinds.TryAcquire(item.Job.Kind, out IAiJobKindLease? lease))
            {
                SetOperationError(Strings.AiPricingUnavailable);
                return;
            }

            using (lease)
            {
                AiJobKindDescriptor descriptor = lease.Descriptor;
                AiJobStatusSemantics status = descriptor.StatusResolver.Resolve(item.Job.Status);
                if (descriptor.RetryHandler is not { } retryHandler
                    || !retryHandler.CanRetry(item.Job, status))
                {
                    SetOperationError(Strings.AiPricingUnavailable);
                    return;
                }

                AiJobRetryPreflight estimate = await retryHandler.GetPreflightAsync(
                    item.Job,
                    _lifetimeCts.Token);
                if (!estimate.CanSubmit)
                {
                    SetOperationError(estimate.Explanation);
                    return;
                }

                await retryHandler.RetryAsync(item.Job, _lifetimeCts.Token);
            }

            if (!IsDisposed)
            {
                await _jobMonitor.RefreshAsync(_lifetimeCts.Token);
            }
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry AI job {JobId}", item.Id);
            SetOperationError(Strings.AiJobCenter_RetryFailed);
        }
    }

    internal async Task<AiJobRetryPreflight> GetRetryEstimateAsync(AiJobItemViewModel item)
    {
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
                    _lifetimeCts.Token);
                SetOperationError(estimate.CanSubmit ? null : estimate.Explanation);
                return estimate;
            }
        }
        catch (AuthenticationRequiredException)
        {
            SetOperationError(Strings.AiAuthenticationRequired);
            return new AiJobRetryPreflight(false, false, Strings.AiAuthenticationRequired);
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
        ArgumentNullException.ThrowIfNull(item);
        if (!item.CanAddToScene)
            return;

        using IDisposable? operation = TryBeginOperation(item);
        if (operation is null)
            return;

        try
        {
            if (!_jobKinds.TryAcquire(item.Job.Kind, out IAiJobKindLease? lease))
                return;

            using (lease)
            {
                AiJobKindDescriptor descriptor = lease.Descriptor;
                AiJobStatusSemantics status = descriptor.StatusResolver.Resolve(item.Job.Status);
                if (!_resultHandlers.TryAcquire(item.Job.Kind, out IAiJobResultHandlerLease? resultHandlerLease))
                {
                    return;
                }

                using (resultHandlerLease)
                {
                    IAiJobResultHandler resultHandler = resultHandlerLease.Handler;
                    if (!resultHandler.CanHandle(item.Job, status))
                        return;

                    await resultHandler.HandleAsync(item.Job, _resultContext, _lifetimeCts.Token);
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

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this)
            ? this
            : _editViewModel.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
        }

        _lifetimeCts.Cancel();
        _disposables.Dispose();
        foreach (AiJobItemViewModel job in _jobs)
        {
            job.Dispose();
        }
        _jobs.Clear();
        IsSelected.Dispose();
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
        _lifetimeCts.Dispose();
    }

    private bool IsDisposed => Volatile.Read(ref _isDisposed);

    private async Task RefreshJobsAsync(bool append)
    {
        SetOperationError(null);
        if (append)
        {
            await _jobMonitor.LoadNextPageAsync(_lifetimeCts.Token);
        }
        else
        {
            await _jobMonitor.RefreshAsync(_lifetimeCts.Token);
        }
    }

    private async Task RefreshEntitlementsAsync()
    {
        try
        {
            await _entitlements.RefreshAsync(_lifetimeCts.Token);
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

        if (Dispatcher.UIThread.CheckAccess())
        {
            ApplySnapshot(snapshot);
        }
        else
        {
            Dispatcher.UIThread.Post(() => ApplySnapshot(snapshot));
        }
    }

    internal void ApplySnapshot(AiJobMonitorSnapshot snapshot)
    {
        lock (_lifetimeGate)
        {
            if (_isDisposed)
                return;

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
    private bool _disposeRequested;
    private bool _isOperationActive;
    private bool _isBusyDisposed;

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

    public bool IsTerminal { get; private set; }

    public bool ShouldPoll { get; private set; }

    public bool IsFailed { get; private set; }

    public bool CanDelete { get; private set; }

    public bool CanAddToScene { get; private set; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public string KindDisplayName { get; private set; } = string.Empty;

    public string StatusDisplayName { get; private set; } = string.Empty;

    internal AiJob Job => _response;

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
        lock (_stateGate)
        {
            if (_disposeRequested)
                return;

            _disposeRequested = true;
            if (!_isOperationActive)
            {
                DisposeBusyProperty();
            }
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
        Error = NormalizeText(_response.Error) switch
        {
            "aiProviderError" => Strings.AiProviderError,
            { } error => error,
            null => null,
        };
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
                status = descriptor.StatusResolver.Resolve(_response.Status);
                canRetry = descriptor.RetryHandler?.CanRetry(_response, status) == true;
            }
        }

        if (_resultHandlers.TryAcquire(_response.Kind, out IAiJobResultHandlerLease? resultHandlerLease))
        {
            using (resultHandlerLease)
            {
                IAiJobResultHandler resultHandler = resultHandlerLease.Handler;
                presentation = resultHandler.Present(_response, status)
                    ?? throw new InvalidOperationException(
                        $"AI job result handler for '{_response.Kind}' returned no presentation.");
                canHandleResult = resultHandler.CanHandle(_response, status);
            }
        }

        Summary = presentation.Summary;
        Details = presentation.Details;
        HasDetails = Details.Length > 0;
        CreatedAtText = _response.CreatedAt.ToLocalTime().ToString("g");
        IsTerminal = status.IsTerminal;
        ShouldPoll = status.ShouldPoll;
        IsFailed = presentation.IsFailure;
        CanDelete = status.IsTerminal;
        CanRetry = canRetry;
        CanAddToScene = canHandleResult;
        KindDisplayName = presentation.KindDisplayName;
        StatusDisplayName = presentation.StatusDisplayName;
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
