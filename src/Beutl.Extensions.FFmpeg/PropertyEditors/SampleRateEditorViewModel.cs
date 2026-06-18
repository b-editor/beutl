using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class SampleRateEditorViewModel : IPropertyEditorContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<SampleRateEditorViewModel>();
    private readonly IPropertyAdapter<int> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<string> _text;
    private readonly FFmpegOptionsCache<int> _cache = new();
    private readonly LatestRefreshTracker _refresh = new();

    private FFmpegAudioEncoderSettings? _settings;
    private string[] _currentSuggestions = [];
    private WeakReference<AutoCompleteStringEditor>? _editorRef;
    private bool _disposed;

    public SampleRateEditorViewModel(
        IPropertyAdapter<int> property,
        PropertyEditorExtension extension)
    {
        _property = property;
        Extension = extension;
        _text = new ReactivePropertySlim<string>(property.GetValue().ToString()).DisposeWith(_disposables);

        // Resolve the FFmpegAudioEncoderSettings from the CorePropertyAdapter.
        if (property is CorePropertyAdapter<int> cpa)
        {
            _settings = cpa.Object as FFmpegAudioEncoderSettings;
        }

        if (_settings != null)
        {
            // Watch for codec changes.
            _settings.GetObservable(FFmpegAudioEncoderSettings.CodecProperty)
                .Subscribe(_ => RequestUpdate())
                .DisposeWith(_disposables);
        }

        // Watch the current value to keep the text in sync.
        _property.GetObservable()
            .Subscribe(value =>
            {
                string text = value.ToString();
                if (_text.Value != text)
                    _text.Value = text;
            })
            .DisposeWith(_disposables);

        // Initialize.
        RequestUpdate();
    }

    public PropertyEditorExtension Extension { get; }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);

        if (visitor is AutoCompleteStringEditor editor)
        {
            editor.Header = _property.DisplayName;
            editor.ItemsSource = _currentSuggestions;

            editor.Bind(AutoCompleteStringEditor.TextProperty, _text.ToBinding())
                .DisposeWith(_disposables);

            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(_disposables);

            _editorRef = new WeakReference<AutoCompleteStringEditor>(editor);
        }
    }

    // Kicks off a non-blocking refresh. A cached result is applied synchronously; otherwise a fresh
    // codec switch supersedes the previous (stale) query via _refresh so it cannot clobber the latest
    // selection. Expected to run on the UI thread (constructor + UI-driven property-change
    // observables); _refresh is not synchronized for concurrent callers.
    private void RequestUpdate()
    {
        if (_settings == null)
        {
            _refresh.Supersede();
            ApplySuggestions([]);
            return;
        }

        QueryParams query = CreateQueryParams(_settings);
        string key = BuildCacheKey(query);
        if (_cache.TryGetCached(key, out int[]? cached))
        {
            _refresh.Supersede();
            ApplySuggestions(cached);
            return;
        }

        CancellationToken ct = _refresh.StartNew();
        _ = UpdateAsync(query, key, ct);
    }

    private async Task UpdateAsync(QueryParams query, string key, CancellationToken ct)
    {
        OptionsQueryResult<int> result;
        try
        {
            result = await _cache
                .GetOrQueryAsync(key, () => QuerySampleRatesAsync(query))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A superseded query is no longer the latest request, so it neither logs nor applies.
            if (LatestRefreshTracker.IsCurrent(ct))
            {
                s_logger.LogWarning(ex, "Failed to refresh sample rates from FFmpeg worker");
                await ApplyOnUiThreadAsync(ct, () => ApplySuggestions([])).ConfigureAwait(false);
            }

            return;
        }

        await ApplyOnUiThreadAsync(ct, () => ApplySuggestions(result.Items)).ConfigureAwait(false);
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
            s_logger.LogDebug(ex, "Dispatcher unavailable while applying sample rates");
        }
    }

    private void ApplySuggestions(int[] supportedRates)
    {
        _currentSuggestions = supportedRates.Select(r => r.ToString()).ToArray();
        if (_editorRef?.TryGetTarget(out var editor) == true)
        {
            editor.ItemsSource = _currentSuggestions;
        }
    }

    private static async Task<OptionsQueryResult<int>> QuerySampleRatesAsync(QueryParams query)
    {
        var connection = await FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().ConfigureAwait(false);
        var response = await connection.RequestAsync<QuerySampleRatesRequest, QuerySampleRatesResponse>(
            MessageType.QuerySampleRates, MessageType.QuerySampleRatesResult,
            new QuerySampleRatesRequest
            {
                CodecName = query.CodecName,
                OutputFile = query.OutputFile
            }).ConfigureAwait(false);
        return new OptionsQueryResult<int>(response.SampleRates, response.Degraded);
    }

    // The cache key and the worker query both derive from this snapshot, so they cannot diverge if
    // _settings mutates mid-flight.
    private readonly record struct QueryParams(string? CodecName, string? OutputFile);

    private static QueryParams CreateQueryParams(FFmpegAudioEncoderSettings settings)
        => new(
            settings.Codec.Equals(CodecRecord.Default) ? null : settings.Codec.Name,
            settings.OutputFile);

    private static string BuildCacheKey(QueryParams query)
        // Use NUL as the delimiter since it cannot appear in a codec name or file path.
        => $"{query.CodecName ?? "<default>"}\0{query.OutputFile}";

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<string> args)
        {
            if (int.TryParse(args.NewValue, out int newValue) && newValue > 0)
            {
                _property.SetValue(newValue);
            }
            else
            {
                // Revert to the current value when the input is invalid.
                _text.Value = _property.GetValue().ToString();
            }
        }
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        _disposed = true;
        _refresh.Dispose();
        _disposables.Dispose();
        _editorRef = null;
        _settings = null;
    }
}
