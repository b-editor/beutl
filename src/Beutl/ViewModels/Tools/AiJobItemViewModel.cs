using System.ComponentModel;
using System.Text.Json;
using Beutl.Api.Services;
using Beutl.Editor.Services.AI;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Tools;

public sealed class AiJobItemViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly ILogger _logger = Log.CreateLogger<AiJobItemViewModel>();
    private readonly object _stateGate = new();
    private readonly IAiJobKindRegistry _jobKinds;
    private readonly AiJobResultRegistry _resultHandlers;
    private AiJob _response;
    private Ref<Bitmap>? _preview;
    private bool _disposeRequested;
    private bool _isOperationActive;
    private bool _retrySubmitted;
    private bool _isBusyDisposed;
    private bool _previewRequested;
    private long _previewLoadGeneration;

    public AiJobItemViewModel(
        AiJob response,
        IAiJobKindRegistry jobKinds,
        AiJobResultRegistry resultHandlers)
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

    public string? Error { get; private set; }

    public bool CanRetry { get; private set; }

    public string? Prompt { get; private set; }

    public string? ImageSize { get; private set; }

    public string? Resolution { get; private set; }

    public string? Language { get; private set; }

    public int? DurationSeconds { get; private set; }

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
        => TryClaimPreviewLoad(out _);

    internal bool TryClaimPreviewLoad(out long loadGeneration)
    {
        lock (_stateGate)
        {
            if (_previewRequested || !HasImagePreview || _disposeRequested)
            {
                loadGeneration = 0;
                return false;
            }

            _previewRequested = true;
            loadGeneration = _previewLoadGeneration;
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

    internal bool IsPreviewLoadCurrent(long loadGeneration)
    {
        lock (_stateGate)
        {
            return !_disposeRequested
                && _previewRequested
                && loadGeneration == _previewLoadGeneration;
        }
    }

    internal void ResetPreviewLoadClaim(long? loadGeneration = null)
    {
        lock (_stateGate)
        {
            if (!_disposeRequested
                && (!loadGeneration.HasValue || loadGeneration.Value == _previewLoadGeneration))
            {
                _previewRequested = false;
            }
        }
    }

    internal void SetPreview(Ref<Bitmap>? preview, long? loadGeneration = null)
    {
        Ref<Bitmap>? previous;
        lock (_stateGate)
        {
            if (_disposeRequested
                || (loadGeneration.HasValue
                    && (!_previewRequested || loadGeneration.Value != _previewLoadGeneration)))
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

    internal void ReleasePreviewForReload()
    {
        Ref<Bitmap>? previous;
        lock (_stateGate)
        {
            if (_disposeRequested)
                return;

            _previewLoadGeneration++;
            _previewRequested = false;
            previous = _preview;
            _preview = null;
            previous?.Dispose();
            if (previous is not null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
            }
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

    internal void MarkRetrySubmitted()
    {
        lock (_stateGate)
        {
            if (_disposeRequested || _retrySubmitted)
                return;

            _retrySubmitted = true;
            CanRetry = false;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanRetry)));
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
        Error = AiErrorMessage.Localize(_response.Error);
        Prompt = GetString("prompt");
        ImageSize = GetString("size");
        Resolution = GetString("resolution");
        Language = GetString("targetLanguage") ?? GetString("language");
        DurationSeconds = GetInt32("durationSeconds");
        var presentation = new AiJobPresentation(
            Kind.Length > 0 ? Kind : Strings.AiJobCenter_NoDescription,
            Status.Length > 0 ? Status : Strings.AiJobCenter_NoDescription,
            Prompt
            ?? GetString("filename")
            ?? Strings.AiJobCenter_NoDescription,
            CreateDetails(),
            false);
        AiJobStatusSemantics status = AiJobStatusSemantics.Unknown;
        try
        {
            status = _jobKinds.GetStatus(_response);
        }
        catch
        {
            status = AiJobStatusSemantics.Unknown;
        }

        bool canRetry = ReadCanRetry(status);
        presentation = ReadPresentation(status, presentation);
        bool canHandleResult = ReadCanApplyResult(status);

        Summary = presentation.Summary;
        Details = presentation.Details;
        HasDetails = Details.Length > 0;
        CreatedAtText = RelativeTimeText.Format(_response.CreatedAt, DateTimeOffset.Now);
        CreatedAtTooltip = _response.CreatedAt.ToLocalTime().ToString("g");
        IsTerminal = status.IsTerminal;
        ShouldPoll = status.ShouldPoll;
        IsFailed = presentation.IsFailure;
        CanDelete = status.IsTerminal;
        CanRetry = canRetry && !_retrySubmitted;
        CanAddToScene = canHandleResult;
        KindDisplayName = presentation.KindDisplayName;
        StatusDisplayName = presentation.StatusDisplayName;
        HasImagePreview = presentation.HasImagePreview && ContentUri is not null;
    }

    // Whether the kind's retry handler takes this job; false without a handler or when it fails.
    private bool ReadCanRetry(AiJobStatusSemantics status)
    {
        bool canRetry = false;
        if (_jobKinds.TryAcquireRetryHandler(
                _response.Kind,
                out IAiJobRetryHandlerLease? retryLease))
        {
            using (retryLease)
            {
                try
                {
                    canRetry = retryLease.Handler.CanRetry(_response, status);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "AI job retry handler for '{Kind}' failed while checking eligibility.",
                        _response.Kind);
                    canRetry = false;
                }
            }
        }

        return canRetry;
    }

    // The kind's own presentation of this job, or fallback without a presenter or when it fails.
    private AiJobPresentation ReadPresentation(AiJobStatusSemantics status, AiJobPresentation fallback)
    {
        AiJobPresentation presentation = fallback;
        if (_resultHandlers.TryAcquirePresenter(
                _response.Kind,
                out IAiJobPresenterLease? presenterLease))
        {
            using (presenterLease)
            {
                try
                {
                    AiJobPresentation candidate = presenterLease.Presenter.Present(_response, status)
                        ?? throw new InvalidOperationException(
                            $"AI job presenter for '{_response.Kind}' returned no presentation.");
                    ValidatePresentation(candidate);
                    presentation = candidate;
                }
                catch
                {
                }
            }
        }

        return presentation;
    }

    // Whether the kind's result applicator takes this job; false without one or when it fails.
    private bool ReadCanApplyResult(AiJobStatusSemantics status)
    {
        bool canHandleResult = false;
        if (_resultHandlers.TryAcquireApplicator(
                _response.Kind,
                out IAiJobResultApplicatorLease? applicatorLease))
        {
            using (applicatorLease)
            {
                try
                {
                    canHandleResult = applicatorLease.Applicator.CanApply(_response, status);
                }
                catch
                {
                    canHandleResult = false;
                }
            }
        }

        return canHandleResult;
    }

    private static void ValidatePresentation(AiJobPresentation presentation)
    {
        if (presentation.KindDisplayName is null
            || presentation.StatusDisplayName is null
            || presentation.Summary is null
            || presentation.Details is null)
        {
            throw new InvalidOperationException(
                "An AI job result handler returned a presentation with a null text field.");
        }
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
