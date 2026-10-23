using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.Logging;
using Beutl.Reactive;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class PixelFormatEditorViewModel
    : CodecOptionEditorViewModel<int, PixelFormatInfo, FFmpegVideoEncoderSettings>
{
    private static readonly ILogger s_logger = Log.CreateLogger<PixelFormatEditorViewModel>();
    private readonly ReactivePropertySlim<int> _selectedIndex;

    private int[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;

    public PixelFormatEditorViewModel(
        IPropertyAdapter<int> property,
        PropertyEditorExtension extension)
        : base(property, extension, FFmpegOptionsCaches.PixelFormats, s_logger, "pixel formats")
    {
        _selectedIndex = new ReactivePropertySlim<int>(-1).DisposeWith(Disposables);
        Initialize(settings => settings.Codec);
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

    protected override CodecQueryParams CreateQuery(FFmpegVideoEncoderSettings settings)
        => CodecOptionQuery.Create(settings.Codec.Value, settings.OutputFile);

    protected override async Task<OptionsQueryResult<PixelFormatInfo>> QueryAsync(CodecQueryParams query)
    {
        var connection = await FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().ConfigureAwait(false);
        var response = await connection.RequestAsync<QueryPixelFormatsRequest, QueryPixelFormatsResponse>(
            MessageType.QueryPixelFormats, MessageType.QueryPixelFormatsResult,
            new QueryPixelFormatsRequest
            {
                CodecName = query.CodecName,
                OutputFile = query.OutputFile
            }).ConfigureAwait(false);
        return new OptionsQueryResult<PixelFormatInfo>(response.Formats, response.Degraded);
    }

    protected override void ApplyResult(OptionsQueryResult<PixelFormatInfo> result) => ApplyFormats(result.Items);

    // Keeps SelectedIndex in sync with the current value.
    protected override void OnValueChanged(int format)
    {
        int index = Array.IndexOf(_currentFormats, format);
        if (index >= 0)
            _selectedIndex.Value = index;
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

        FormatSelectionResult selection =
            CodecFormatSelection.Resolve(_currentFormats, Property.GetValue(), FFPixelFormat.None);
        _selectedIndex.Value = -1; // Force a transition so the binding re-fires even if the index is unchanged.
        if (selection.ResetToSentinel)
        {
            Property.SetValue(FFPixelFormat.None);
        }

        _selectedIndex.Value = selection.SelectedIndex;
    }

    protected override void ApplyFallback()
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
