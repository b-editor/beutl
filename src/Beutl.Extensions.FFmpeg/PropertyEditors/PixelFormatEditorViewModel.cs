using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class PixelFormatEditorViewModel : IPropertyEditorContext
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(PixelFormatEditorViewModel));
    private readonly IPropertyAdapter<int> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<int> _selectedIndex;
    private readonly FFmpegOptionsCache<PixelFormatInfo> _cache = new();

    private FFmpegVideoEncoderSettings? _settings;
    private int[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;
    private CancellationTokenSource? _updateCts;

    public PixelFormatEditorViewModel(
        IPropertyAdapter<int> property,
        PropertyEditorExtension extension)
    {
        _property = property;
        Extension = extension;
        _selectedIndex = new ReactivePropertySlim<int>(-1).DisposeWith(_disposables);

        // CorePropertyAdapterからFFmpegVideoEncoderSettingsを取得
        if (property is CorePropertyAdapter<int> cpa)
        {
            _settings = cpa.Object as FFmpegVideoEncoderSettings;
        }

        if (_settings != null)
        {
            // コーデック変更を監視
            _settings.GetObservable(FFmpegVideoEncoderSettings.CodecProperty)
                .Subscribe(_ => RequestUpdate())
                .DisposeWith(_disposables);
        }

        // 現在値の変更を監視してSelectedIndexを更新
        _property.GetObservable()
            .Subscribe(format =>
            {
                int index = Array.IndexOf(_currentFormats, format);
                if (index >= 0)
                    _selectedIndex.Value = index;
            })
            .DisposeWith(_disposables);

        // 初期化
        RequestUpdate();
    }

    public PropertyEditorExtension Extension { get; }

    public void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);

        if (visitor is EnumEditor editor)
        {
            editor.Header = _property.DisplayName;
            editor.Items = _currentItems;

            editor.Bind(EnumEditor.SelectedIndexProperty, _selectedIndex.ToBinding())
                .DisposeWith(_disposables);

            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(_disposables);

            _editorRef = new WeakReference<EnumEditor>(editor);
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
            ApplyFallback();
            return;
        }

        string key = BuildCacheKey(_settings);
        if (_cache.TryGetCached(key, out PixelFormatInfo[]? cached))
        {
            ApplyFormats(cached);
            return;
        }

        _ = UpdateAsync(_settings, key, cts.Token);
    }

    private async Task UpdateAsync(FFmpegVideoEncoderSettings settings, string key, CancellationToken ct)
    {
        try
        {
            PixelFormatInfo[] formatInfos = await _cache
                .GetOrQueryAsync(key, () => QueryPixelFormatsAsync(settings))
                .ConfigureAwait(false);

            // The cancellation check runs on the UI thread, where RequestUpdate cancels the token,
            // so a superseded codec selection can never clobber the latest one.
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!ct.IsCancellationRequested)
                    ApplyFormats(formatInfos);
            });
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
                return;

            try
            {
                s_logger.LogWarning(ex, "Failed to query pixel formats from FFmpeg worker");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                        ApplyFallback();
                });
            }
            catch (Exception dispatchEx)
            {
                // The dispatcher can fault during shutdown; never let it escape this fire-and-forget task.
                s_logger.LogDebug(dispatchEx, "Dispatcher unavailable while applying pixel-format fallback");
            }
        }
    }

    private void ApplyFormats(PixelFormatInfo[] formatInfos)
    {
        // FFPixelFormat.None ("Auto") を先頭に追加
        _currentFormats = [FFPixelFormat.None, .. formatInfos.Select(f => f.Value)];
        _currentItems = new EnumItem[] { new("Auto", null, FFPixelFormat.None) }
            .Concat(formatInfos.Select(f => new EnumItem(f.Name, null, f.Value)))
            .ToArray();

        // エディタのアイテムを更新
        if (_editorRef?.TryGetTarget(out var editor) == true)
        {
            editor.Items = _currentItems;
        }

        // 現在値がリストにあるか確認
        var currentValue = _property.GetValue();
        int index = Array.IndexOf(_currentFormats, currentValue);
        _selectedIndex.Value = -1; // 一旦リセット
        if (index < 0 && currentValue != FFPixelFormat.None)
        {
            // 非対応フォーマットが選択されていたらAutoにリセット
            _property.SetValue(FFPixelFormat.None);
            _selectedIndex.Value = 0;
        }
        else
        {
            _selectedIndex.Value = Math.Max(index, 0);
        }
    }

    private void ApplyFallback()
    {
        // エラー時はAutoのみ表示
        _currentFormats = [FFPixelFormat.None];
        _currentItems = [new EnumItem("Auto", null, FFPixelFormat.None)];
        _selectedIndex.Value = 0;

        if (_editorRef?.TryGetTarget(out var editor) == true)
        {
            editor.Items = _currentItems;
        }
    }

    private static async Task<PixelFormatInfo[]> QueryPixelFormatsAsync(FFmpegVideoEncoderSettings settings)
    {
        var connection = await FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().ConfigureAwait(false);
        var response = await connection.RequestAsync<QueryPixelFormatsRequest, QueryPixelFormatsResponse>(
            MessageType.QueryPixelFormats, MessageType.QueryPixelFormatsResult,
            new QueryPixelFormatsRequest
            {
                CodecName = settings.Codec.Equals(CodecRecord.Default) ? null : settings.Codec.Name,
                OutputFile = settings.OutputFile
            }).ConfigureAwait(false);
        return response.Formats;
    }

    private static string BuildCacheKey(FFmpegVideoEncoderSettings settings)
    {
        string codec = settings.Codec.Equals(CodecRecord.Default) ? "<default>" : settings.Codec.Name;
        return $"{codec}|{settings.OutputFile}";
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<int> args)
        {
            int newIndex = Math.Clamp(args.NewValue, 0, _currentFormats.Length - 1);
            var newValue = _currentFormats[newIndex];
            _property.SetValue(newValue);
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
