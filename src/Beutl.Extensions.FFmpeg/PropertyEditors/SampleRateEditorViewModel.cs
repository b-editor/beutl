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

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class SampleRateEditorViewModel
    : CodecOptionEditorViewModel<int, int, FFmpegAudioEncoderSettings>
{
    private static readonly ILogger s_logger = Log.CreateLogger<SampleRateEditorViewModel>();
    private readonly ReactivePropertySlim<string> _text;

    private string[] _currentSuggestions = [];
    private WeakReference<AutoCompleteStringEditor>? _editorRef;

    public SampleRateEditorViewModel(
        IPropertyAdapter<int> property,
        PropertyEditorExtension extension)
        : base(property, extension, FFmpegOptionsCaches.SampleRates, s_logger, "sample rates")
    {
        _text = new ReactivePropertySlim<string>(property.GetValue().ToString()).DisposeWith(Disposables);
        Initialize(FFmpegAudioEncoderSettings.CodecProperty);
    }

    public override void Accept(IPropertyEditorContextVisitor visitor)
    {
        visitor.Visit(this);

        if (visitor is AutoCompleteStringEditor editor)
        {
            editor.Header = Property.DisplayName;
            editor.ItemsSource = _currentSuggestions;

            editor.Bind(AutoCompleteStringEditor.TextProperty, _text.ToBinding())
                .DisposeWith(Disposables);

            editor.AddDisposableHandler(PropertyEditor.ValueConfirmedEvent, OnValueConfirmed)
                .DisposeWith(Disposables);

            _editorRef = new WeakReference<AutoCompleteStringEditor>(editor);
        }
    }

    protected override CodecQueryParams CreateQuery(FFmpegAudioEncoderSettings settings)
        => CodecOptionQuery.Create(settings.Codec, settings.OutputFile);

    protected override async Task<OptionsQueryResult<int>> QueryAsync(CodecQueryParams query)
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

    protected override void ApplyResult(OptionsQueryResult<int> result) => ApplySuggestions(result.Items);

    protected override void ApplyFallback() => ApplySuggestions([]);

    // Keeps the text in sync with the current value.
    protected override void OnValueChanged(int value)
    {
        string text = value.ToString();
        if (_text.Value != text)
            _text.Value = text;
    }

    private void ApplySuggestions(int[] supportedRates)
    {
        _currentSuggestions = supportedRates.Select(r => r.ToString()).ToArray();
        if (_editorRef?.TryGetTarget(out var editor) == true)
        {
            editor.ItemsSource = _currentSuggestions;
        }
    }

    private void OnValueConfirmed(object? sender, PropertyEditorValueChangedEventArgs e)
    {
        if (e is PropertyEditorValueChangedEventArgs<string> args)
        {
            if (int.TryParse(args.NewValue, out int newValue) && newValue > 0)
            {
                Property.SetValue(newValue);
            }
            else
            {
                // Revert to the current value when the input is invalid.
                _text.Value = Property.GetValue().ToString();
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _editorRef = null;
    }
}
