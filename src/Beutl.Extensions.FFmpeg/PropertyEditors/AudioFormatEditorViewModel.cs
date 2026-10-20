using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Beutl.Reactive;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class AudioFormatEditorViewModel
    : CodecOptionEditorViewModel<AudioFormat, AudioFormat, FFmpegAudioEncoderSettings>
{
    private static readonly ILogger s_logger = Log.CreateLogger<AudioFormatEditorViewModel>();
    private readonly ReactivePropertySlim<int> _selectedIndex;

    private AudioFormat[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;

    public AudioFormatEditorViewModel(
        IPropertyAdapter<AudioFormat> property,
        PropertyEditorExtension extension)
        : base(property, extension, FFmpegOptionsCaches.AudioFormats, s_logger, "audio formats")
    {
        _selectedIndex = new ReactivePropertySlim<int>(-1).DisposeWith(Disposables);
        Initialize(FFmpegAudioEncoderSettings.CodecProperty);
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);

        if (visitor is EnumEditor editor)
        {
            editor.Header = Property.DisplayName;
            editor.Items = _currentItems;

            editor.Bind(EnumEditor.SelectedIndexProperty, _selectedIndex.ToBinding())
                .DisposeWith(Disposables);

            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);

            _editorRef = new WeakReference<EnumEditor>(editor);
        }
    }

    protected override CodecQueryParams CreateQuery(FFmpegAudioEncoderSettings settings)
        => CodecOptionQuery.Create(settings.Codec, settings.OutputFile);

    protected override async Task<OptionsQueryResult<AudioFormat>> QueryAsync(CodecQueryParams query)
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

    protected override void ApplyResult(OptionsQueryResult<AudioFormat> result)
        => ApplyFormats(AudioFormatOptions.ResolveSupported(result));

    protected override void ApplyFallback() => ApplyFormats(AudioFormatOptions.All());

    // Keeps SelectedIndex in sync with the current value.
    protected override void OnValueChanged(AudioFormat format)
    {
        int index = Array.IndexOf(_currentFormats, format);
        if (index >= 0)
            _selectedIndex.Value = index;
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
            CodecFormatSelection.Resolve(_currentFormats, Property.GetValue(), AudioFormat.Default);
        _selectedIndex.Value = -1; // Force a transition so the binding re-fires even if the index is unchanged.
        if (selection.ResetToSentinel)
        {
            Property.SetValue(AudioFormat.Default);
        }

        _selectedIndex.Value = selection.SelectedIndex;
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e.Expect<int>(sender) is { } args)
        {
            int newIndex = Math.Clamp(args.NewValue, 0, _currentFormats.Length - 1);
            var newValue = _currentFormats[newIndex];
            Property.SetValue(newValue);
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _editorRef = null;
    }
}
