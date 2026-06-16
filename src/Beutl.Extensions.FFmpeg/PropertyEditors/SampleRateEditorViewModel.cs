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
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(SampleRateEditorViewModel));
    private readonly IPropertyAdapter<int> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<string> _text;
    private readonly FFmpegOptionsCache<int> _cache = new();

    private FFmpegAudioEncoderSettings? _settings;
    private string[] _currentSuggestions = [];
    private WeakReference<AutoCompleteStringEditor>? _editorRef;
    private CancellationTokenSource? _updateCts;

    public SampleRateEditorViewModel(
        IPropertyAdapter<int> property,
        PropertyEditorExtension extension)
    {
        _property = property;
        Extension = extension;
        _text = new ReactivePropertySlim<string>(property.GetValue().ToString()).DisposeWith(_disposables);

        // CorePropertyAdapterからFFmpegAudioEncoderSettingsを取得
        if (property is CorePropertyAdapter<int> cpa)
        {
            _settings = cpa.Object as FFmpegAudioEncoderSettings;
        }

        if (_settings != null)
        {
            // コーデック変更を監視
            _settings.GetObservable(FFmpegAudioEncoderSettings.CodecProperty)
                .Subscribe(_ => RequestUpdate())
                .DisposeWith(_disposables);
        }

        // 現在値の変更を監視してテキストを更新
        _property.GetObservable()
            .Subscribe(value =>
            {
                string text = value.ToString();
                if (_text.Value != text)
                    _text.Value = text;
            })
            .DisposeWith(_disposables);

        // 初期化
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

    // Kicks off a non-blocking refresh; cached results are applied immediately and a fresh codec
    // switch cancels the previous (stale) query so it cannot clobber the latest selection.
    // Always invoked on the UI thread (constructor + UI-driven property-change observables), so the
    // _updateCts swap below needs no synchronization.
    private void RequestUpdate()
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        var cts = _updateCts = new CancellationTokenSource();

        if (_settings == null)
        {
            ApplySuggestions([]);
            return;
        }

        string key = BuildCacheKey(_settings);
        if (_cache.TryGetCached(key, out int[]? cached))
        {
            ApplySuggestions(cached);
            return;
        }

        _ = UpdateAsync(_settings, key, cts.Token);
    }

    private async Task UpdateAsync(FFmpegAudioEncoderSettings settings, string key, CancellationToken ct)
    {
        try
        {
            int[] supportedRates = await _cache
                .GetOrQueryAsync(key, () => QuerySampleRatesAsync(settings))
                .ConfigureAwait(false);

            // The cancellation check runs on the UI thread, where RequestUpdate cancels the token,
            // so a superseded codec selection can never clobber the latest one.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                    ApplySuggestions(supportedRates);
            });
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
                return;

            try
            {
                s_logger.LogWarning(ex, "Failed to query sample rates from FFmpeg worker");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                        ApplySuggestions(Array.Empty<int>());
                });
            }
            catch (Exception dispatchEx)
            {
                // The dispatcher can fault during shutdown; never let it escape this fire-and-forget task.
                s_logger.LogDebug(dispatchEx, "Dispatcher unavailable while applying sample-rate fallback");
            }
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

    private static async Task<int[]> QuerySampleRatesAsync(FFmpegAudioEncoderSettings settings)
    {
        var connection = await FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().ConfigureAwait(false);
        var response = await connection.RequestAsync<QuerySampleRatesRequest, QuerySampleRatesResponse>(
            MessageType.QuerySampleRates, MessageType.QuerySampleRatesResult,
            new QuerySampleRatesRequest
            {
                CodecName = settings.Codec.Equals(CodecRecord.Default) ? null : settings.Codec.Name,
                OutputFile = settings.OutputFile
            }).ConfigureAwait(false);
        return response.SampleRates;
    }

    private static string BuildCacheKey(FFmpegAudioEncoderSettings settings)
    {
        string codec = settings.Codec.Equals(CodecRecord.Default) ? "<default>" : settings.Codec.Name;
        return $"{codec}|{settings.OutputFile}";
    }

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
                // 無効な値の場合、現在値に戻す
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
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = null;
        _disposables.Dispose();
        _editorRef = null;
        _settings = null;
    }
}
