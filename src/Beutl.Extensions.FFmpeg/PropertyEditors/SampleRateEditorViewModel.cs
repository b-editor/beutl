using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
#if FFMPEG_OUT_OF_PROCESS
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
#else
using FFmpegSharp;
#endif
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Reactive.Bindings;
using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class SampleRateEditorViewModel : IPropertyEditorContext
{
    private readonly IPropertyAdapter<int> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<string> _text;

    private FFmpegAudioEncoderSettings? _settings;
    private string[] _currentSuggestions = [];
    private WeakReference<AutoCompleteStringEditor>? _editorRef;

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
                .Subscribe(_ => UpdateSampleRates())
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
        UpdateSampleRates();
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

    private void UpdateSampleRates()
    {
        try
        {
            int[] supportedRates = GetCodecSampleRates(_settings);

            _currentSuggestions = supportedRates
                .Select(r => r.ToString())
                .ToArray();

            // エディタのItemsSourceを更新
            if (_editorRef?.TryGetTarget(out var editor) == true)
            {
                editor.ItemsSource = _currentSuggestions;
            }
        }
        catch
        {
            // エラー時は補完候補なし（自由入力のみ）
            _currentSuggestions = [];

            if (_editorRef?.TryGetTarget(out var editor) == true)
            {
                editor.ItemsSource = _currentSuggestions;
            }
        }
    }

    private static int[] GetCodecSampleRates(FFmpegAudioEncoderSettings? settings)
    {
        if (settings == null) return [];

        try
        {
#if FFMPEG_OUT_OF_PROCESS
            var connection = FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().GetAwaiter().GetResult();
            var response = connection.RequestAsync<QuerySampleRatesRequest, QuerySampleRatesResponse>(
                MessageType.QuerySampleRates, MessageType.QuerySampleRatesResult,
                new QuerySampleRatesRequest
                {
                    CodecName = settings.Codec.Equals(CodecRecord.Default) ? null : settings.Codec.Name,
                    OutputFile = settings.OutputFile
                }).AsTask().GetAwaiter().GetResult();
            return response.SampleRates;
#else
            MediaCodec codec;
            if (settings.Codec.Equals(CodecRecord.Default))
            {
                string? outputFile = settings.OutputFile;
                if (string.IsNullOrEmpty(outputFile))
                    return [];

                var outFormat = OutputFormat.GuessFormat(null, outputFile, null);
                codec = MediaCodec.FindEncoder(outFormat.AudioCodec);
            }
            else
            {
                codec = MediaCodec.FindEncoder(settings.Codec.Name);
            }

            return codec.GetSupportedSamplerates().ToArray();
#endif
        }
        catch
        {
            return [];
        }
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
        _disposables.Dispose();
        _editorRef = null;
        _settings = null;
    }
}
