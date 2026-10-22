using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>
/// Shared refresh logic of the codec property editors (audio format / pixel format / sample rate): queries
/// the FFmpeg worker through a shared cache whenever the codec changes and applies only the latest result.
/// </summary>
internal abstract class CodecOptionEditorViewModel<TValue, TItem, TSettings> : IPropertyEditorContext
    where TSettings : class, ICoreObject
{
    private readonly FFmpegOptionsCache<TItem> _cache;
    private readonly ILogger _logger;
    private readonly string _optionsName;
    private readonly LatestRefreshTracker _refresh = new();

    private TSettings? _settings;
    private bool _disposed;

    protected CodecOptionEditorViewModel(
        IPropertyAdapter<TValue> property,
        PropertyEditorExtension extension,
        FFmpegOptionsCache<TItem> cache,
        ILogger logger,
        string optionsName)
    {
        Property = property;
        Extension = extension;
        _cache = cache;
        _logger = logger;
        _optionsName = optionsName;

        // Resolve the encoder settings from the property's adapter.
        if (property is EnginePropertyAdapter<TValue> adapter)
        {
            _settings = adapter.Object as TSettings;
        }
    }

    public PropertyEditorExtension Extension { get; }

    protected IPropertyAdapter<TValue> Property { get; }

    protected CompositeDisposable Disposables { get; } = [];

    public abstract void Accept(IPropertyEditorContextVisitor visitor);

    // Called at the end of the derived constructor, once the derived state exists.
    protected void Initialize(Func<TSettings, IProperty<CodecRecord>> codecProperty)
    {
        if (_settings != null)
        {
            // Watch for codec changes.
            codecProperty(_settings).GetObservable()
                .Subscribe(_ => RequestUpdate())
                .DisposeWith(Disposables);
        }

        Property.GetObservable()
            .Subscribe(OnValueChanged)
            .DisposeWith(Disposables);

        // Initialize.
        RequestUpdate();
    }

    protected abstract CodecQueryParams CreateQuery(TSettings settings);

    protected abstract Task<OptionsQueryResult<TItem>> QueryAsync(CodecQueryParams query);

    // Applies a worker answer, whether cached or just queried.
    protected abstract void ApplyResult(OptionsQueryResult<TItem> result);

    // Applies the options shown when the worker cannot be asked.
    protected abstract void ApplyFallback();

    protected abstract void OnValueChanged(TValue? value);

    // Kicks off a non-blocking refresh. A cached result is applied synchronously; otherwise a fresh
    // codec switch supersedes the previous (stale) query via _refresh so it cannot clobber the latest
    // selection. Expected to run on the UI thread (constructor + UI-driven property-change
    // observables); _refresh is not synchronized for concurrent callers.
    private void RequestUpdate()
    {
        if (_settings == null)
        {
            _refresh.Supersede();
            ApplyFallback();
            return;
        }

        CodecQueryParams query = CreateQuery(_settings);
        string key = CodecOptionQuery.BuildCacheKey(query);
        if (_cache.TryGetCached(key, out TItem[]? cached))
        {
            _refresh.Supersede();
            ApplyResult(new OptionsQueryResult<TItem>(cached, Degraded: false));
            return;
        }

        CancellationToken ct = _refresh.StartNew();
        _ = UpdateAsync(query, key, ct);
    }

    private async Task UpdateAsync(CodecQueryParams query, string key, CancellationToken ct)
    {
        OptionsQueryResult<TItem> result;
        try
        {
            result = await _cache
                .GetOrQueryAsync(key, () => QueryAsync(query))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A superseded query is no longer the latest request, so it neither logs nor applies.
            if (LatestRefreshTracker.IsCurrent(ct))
            {
                if (ex is not FFmpegLibrariesNotFoundException)
                    _logger.LogWarning(ex, "Failed to refresh {Options} from FFmpeg worker", _optionsName);
                await ApplyOnUiThreadAsync(ct, ApplyFallback).ConfigureAwait(false);
            }

            return;
        }

        await ApplyOnUiThreadAsync(ct, () => ApplyResult(result)).ConfigureAwait(false);
    }

    // Marshals the apply back to the UI thread, where _refresh is mutated, and applies only while
    // this request is still the latest and the editor is alive. A dispatcher fault during teardown is
    // logged distinctly from a worker-query failure and never escapes this fire-and-forget task.
    private async Task ApplyOnUiThreadAsync(CancellationToken ct, Action apply)
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && LatestRefreshTracker.IsCurrent(ct))
                    apply();
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Dispatcher unavailable while applying {Options}", _optionsName);
        }
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public virtual void Dispose()
    {
        _disposed = true;
        _refresh.Dispose();
        Disposables.Dispose();
        _settings = null;
    }
}
