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
using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class AudioFormatEditorViewModel : IPropertyEditorContext
{
    private static readonly ILogger s_logger = Log.CreateLogger<AudioFormatEditorViewModel>();
    private readonly IPropertyAdapter<AudioFormat> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<int> _selectedIndex;
    private readonly LatestRefreshTracker _refresh = new();

    private FFmpegAudioEncoderSettings? _settings;
    private AudioFormat[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;
    private bool _disposed;

    public AudioFormatEditorViewModel(
        IPropertyAdapter<AudioFormat> property,
        PropertyEditorExtension extension)
    {
        _property = property;
        Extension = extension;
        _selectedIndex = new ReactivePropertySlim<int>(-1).DisposeWith(_disposables);

        // Resolve the FFmpegAudioEncoderSettings from the CorePropertyAdapter.
        if (property is CorePropertyAdapter<AudioFormat> cpa)
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
            ApplyFormats(AudioFormatOptions.All());
            return;
        }

        CodecQueryParams query = CodecOptionQuery.Create(_settings.Codec, _settings.OutputFile);
        string key = CodecOptionQuery.BuildCacheKey(query);
        if (FFmpegOptionsCaches.AudioFormats.TryGetCached(key, out AudioFormat[]? cached))
        {
            _refresh.Supersede();
            ApplyFormats(cached);
            return;
        }

        CancellationToken ct = _refresh.StartNew();
        _ = UpdateAsync(query, key, ct);
    }

    private async Task UpdateAsync(CodecQueryParams query, string key, CancellationToken ct)
    {
        OptionsQueryResult<AudioFormat> result;
        try
        {
            result = await FFmpegOptionsCaches.AudioFormats
                .GetOrQueryAsync(key, () => QueryAudioFormatsAsync(query))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A superseded query is no longer the latest request, so it neither logs nor applies.
            if (LatestRefreshTracker.IsCurrent(ct))
            {
                s_logger.LogWarning(ex, "Failed to refresh audio formats from FFmpeg worker");
                await ApplyOnUiThreadAsync(ct, () => ApplyFormats(AudioFormatOptions.All())).ConfigureAwait(false);
            }

            return;
        }

        AudioFormat[] formats = AudioFormatOptions.ResolveSupported(result);
        await ApplyOnUiThreadAsync(ct, () => ApplyFormats(formats)).ConfigureAwait(false);
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
            s_logger.LogDebug(ex, "Dispatcher unavailable while applying audio formats");
        }
    }

    private void ApplyFormats(AudioFormat[] supportedFmts)
    {
        // Prepend Default ("Auto") to the list.
        _currentFormats = [AudioFormat.Default, .. supportedFmts];
        _currentItems = _currentFormats
            .Select(f => new EnumItem(
                f == AudioFormat.Default ? "Auto" : f.ToString(),
                null,
                f))
            .ToArray();

        // Update the editor's items.
        if (_editorRef?.TryGetTarget(out var editor) == true)
        {
            editor.Items = _currentItems;
        }

        FormatSelectionResult selection =
            CodecFormatSelection.Resolve(_currentFormats, _property.GetValue(), AudioFormat.Default);
        _selectedIndex.Value = -1; // Force a transition so the binding re-fires even if the index is unchanged.
        if (selection.ResetToSentinel)
        {
            _property.SetValue(AudioFormat.Default);
        }

        _selectedIndex.Value = selection.SelectedIndex;
    }

    private static async Task<OptionsQueryResult<AudioFormat>> QueryAudioFormatsAsync(CodecQueryParams query)
    {
        var connection = await FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().ConfigureAwait(false);
        var response = await connection.RequestAsync<QueryAudioFormatsRequest, QueryAudioFormatsResponse>(
            MessageType.QueryAudioFormats, MessageType.QueryAudioFormatsResult,
            new QueryAudioFormatsRequest
            {
                CodecName = query.CodecName,
                OutputFile = query.OutputFile
            }).ConfigureAwait(false);
        return new OptionsQueryResult<AudioFormat>(
            response.Formats.Select(f => (AudioFormat)f).ToArray(), response.Degraded);
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
