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
    private static readonly ILogger s_logger = Log.CreateLogger<PixelFormatEditorViewModel>();
    private readonly IPropertyAdapter<int> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<int> _selectedIndex;
    private readonly FFmpegOptionsCache<PixelFormatInfo> _cache = new();
    private readonly LatestRefreshTracker _refresh = new();

    private FFmpegVideoEncoderSettings? _settings;
    private int[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;
    private bool _disposed;

    public PixelFormatEditorViewModel(
        IPropertyAdapter<int> property,
        PropertyEditorExtension extension)
    {
        _property = property;
        Extension = extension;
        _selectedIndex = new ReactivePropertySlim<int>(-1).DisposeWith(_disposables);

        // Resolve the FFmpegVideoEncoderSettings from the CorePropertyAdapter.
        if (property is CorePropertyAdapter<int> cpa)
        {
            _settings = cpa.Object as FFmpegVideoEncoderSettings;
        }

        if (_settings != null)
        {
            // Watch for codec changes.
            _settings.GetObservable(FFmpegVideoEncoderSettings.CodecProperty)
                .Subscribe(_ => RequestUpdate())
                .DisposeWith(_disposables);
        }

        // Watch the current value to keep SelectedIndex in sync.
        _property.GetObservable()
            .Subscribe(format =>
            {
                int index = Array.IndexOf(_currentFormats, format);
                if (index >= 0)
                    _selectedIndex.Value = index;
            })
            .DisposeWith(_disposables);

        // Initialize.
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

        string key = BuildCacheKey(_settings);
        if (_cache.TryGetCached(key, out PixelFormatInfo[]? cached))
        {
            _refresh.Supersede();
            ApplyFormats(cached);
            return;
        }

        CancellationToken ct = _refresh.StartNew();
        _ = UpdateAsync(_settings, key, ct);
    }

    private async Task UpdateAsync(FFmpegVideoEncoderSettings settings, string key, CancellationToken ct)
    {
        PixelFormatInfo[] formatInfos;
        try
        {
            formatInfos = await _cache
                .GetOrQueryAsync(key, () => QueryPixelFormatsAsync(settings))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A superseded query is no longer the latest request, so it neither logs nor applies.
            if (LatestRefreshTracker.IsCurrent(ct))
            {
                s_logger.LogWarning(ex, "Failed to refresh pixel formats from FFmpeg worker");
                await ApplyOnUiThreadAsync(ct, ApplyFallback).ConfigureAwait(false);
            }

            return;
        }

        await ApplyOnUiThreadAsync(ct, () => ApplyFormats(formatInfos)).ConfigureAwait(false);
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
            s_logger.LogDebug(ex, "Dispatcher unavailable while applying pixel formats");
        }
    }

    private void ApplyFormats(PixelFormatInfo[] formatInfos)
    {
        // Prepend FFPixelFormat.None ("Auto") to the list.
        _currentFormats = [FFPixelFormat.None, .. formatInfos.Select(f => f.Value)];
        _currentItems = new EnumItem[] { new("Auto", null, FFPixelFormat.None) }
            .Concat(formatInfos.Select(f => new EnumItem(f.Name, null, f.Value)))
            .ToArray();

        // Update the editor's items.
        if (_editorRef?.TryGetTarget(out var editor) == true)
        {
            editor.Items = _currentItems;
        }

        // Check whether the current value is present in the list.
        var currentValue = _property.GetValue();
        int index = Array.IndexOf(_currentFormats, currentValue);
        _selectedIndex.Value = -1; // Reset once.
        if (index < 0 && currentValue != FFPixelFormat.None)
        {
            // Reset to Auto when an unsupported format is selected.
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
        // On error, show only Auto.
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
        // Use NUL as the delimiter since it cannot appear in a codec name or file path.
        return $"{codec}\0{settings.OutputFile}";
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
        _disposed = true;
        _refresh.Dispose();
        _disposables.Dispose();
        _editorRef = null;
        _settings = null;
    }
}
