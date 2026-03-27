using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Interactivity;
using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using FFmpegSharp;
using Reactive.Bindings;

#if FFMPEG_BUILD_IN
using Beutl.Embedding.FFmpeg.Encoding;
using AudioFormat = Beutl.Embedding.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;

namespace Beutl.Embedding.FFmpeg.PropertyEditors;
#else
using Beutl.Extensions.FFmpeg.Encoding;
using AudioFormat = Beutl.Extensions.FFmpeg.Encoding.FFmpegAudioEncoderSettings.AudioFormat;
namespace Beutl.Extensions.FFmpeg.PropertyEditors;
#endif

internal sealed class AudioFormatEditorViewModel : IPropertyEditorContext
{
    private readonly IPropertyAdapter<AudioFormat> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<int> _selectedIndex;

    private FFmpegAudioEncoderSettings? _settings;
    private AudioFormat[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;

    public AudioFormatEditorViewModel(
        IPropertyAdapter<AudioFormat> property,
        PropertyEditorExtension extension)
    {
        _property = property;
        Extension = extension;
        _selectedIndex = new ReactivePropertySlim<int>(-1).DisposeWith(_disposables);

        // CorePropertyAdapterからFFmpegAudioEncoderSettingsを取得
        if (property is CorePropertyAdapter<AudioFormat> cpa)
        {
            _settings = cpa.Object as FFmpegAudioEncoderSettings;
        }

        if (_settings != null)
        {
            // コーデック変更を監視
            _settings.GetObservable(FFmpegAudioEncoderSettings.CodecProperty)
                .Subscribe(_ => UpdateAudioFormats())
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
        UpdateAudioFormats();
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

    private void UpdateAudioFormats()
    {
        try
        {
            AudioFormat[] supportedFmts = _settings != null
                ? GetCodecFormats(_settings)
                : GetAllFormats();

            // AV_PIX_FMT_NONE ("Auto") を先頭に追加
            _currentFormats = [AudioFormat.Default, .. supportedFmts];
            _currentItems = _currentFormats
                .Select(f => new EnumItem(
                    f == AudioFormat.Default ? "Auto" : f.ToString(),
                    null,
                    f))
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
            if (index < 0 && currentValue != AudioFormat.Default)
            {
                // 非対応フォーマットが選択されていたらAutoにリセット
                _property.SetValue(AudioFormat.Default);
                _selectedIndex.Value = 0;
            }
            else
            {
                _selectedIndex.Value = Math.Max(index, 0);
            }
        }
        catch
        {
            // エラー時はAutoのみ表示
            _currentFormats = [AudioFormat.Default];
            _currentItems = [new EnumItem("Auto", null, AudioFormat.Default)];
            _selectedIndex.Value = 0;

            if (_editorRef?.TryGetTarget(out var editor) == true)
            {
                editor.Items = _currentItems;
            }
        }
    }

    private static AudioFormat[] GetCodecFormats(FFmpegAudioEncoderSettings settings)
    {
        MediaCodec codec;
        if (settings.Codec.Equals(CodecRecord.Default))
        {
            string? outputFile = settings.OutputFile;
            if (string.IsNullOrEmpty(outputFile))
            {
                return GetAllFormats();
            }

            var outFormat = OutputFormat.GuessFormat(null, outputFile, null);
            codec = MediaCodec.FindEncoder(outFormat.AudioCodec);
        }
        else
        {
            codec = MediaCodec.FindEncoder(settings.Codec.Name);
        }

        var fmts = codec.GetSampelFmts().Select(f => (AudioFormat)f).ToArray();

        return fmts.Length > 0 ? fmts : GetAllFormats();
    }

    private static AudioFormat[] GetAllFormats()
    {
        return Enum.GetValues<AudioFormat>()
            .ToArray();
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
        _disposables.Dispose();
        _editorRef = null;
        _settings = null;
    }
}
