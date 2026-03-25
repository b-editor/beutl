using System.Reactive.Disposables;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.PropertyAdapters;
using Beutl.Reactive;

using FFmpeg.AutoGen.Abstractions;

using FFmpegSharp;

using Reactive.Bindings;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

internal sealed class PixelFormatEditorViewModel : IPropertyEditorContext
{
    private readonly IPropertyAdapter<AVPixelFormat> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<int> _selectedIndex;

    private FFmpegVideoEncoderSettings? _settings;
    private AVPixelFormat[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;

    public PixelFormatEditorViewModel(
        IPropertyAdapter<AVPixelFormat> property,
        PropertyEditorExtension extension)
    {
        _property = property;
        Extension = extension;
        _selectedIndex = new ReactivePropertySlim<int>(-1).DisposeWith(_disposables);

        // CorePropertyAdapterからFFmpegVideoEncoderSettingsを取得
        if (property is CorePropertyAdapter<AVPixelFormat> cpa)
        {
            _settings = cpa.Object as FFmpegVideoEncoderSettings;
        }

        if (_settings != null)
        {
            // コーデック変更を監視
            _settings.GetObservable(FFmpegVideoEncoderSettings.CodecProperty)
                .Subscribe(_ => UpdatePixelFormats())
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
        UpdatePixelFormats();
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

    private void UpdatePixelFormats()
    {
        try
        {
            AVPixelFormat[] supportedFmts = _settings != null
                ? GetCodecFormats(_settings)
                : GetAllFormats();

            // AV_PIX_FMT_NONE ("Auto") を先頭に追加
            _currentFormats = [AVPixelFormat.AV_PIX_FMT_NONE, .. supportedFmts];
            _currentItems = _currentFormats
                .Select(f => new EnumItem(
                    f == AVPixelFormat.AV_PIX_FMT_NONE ? "Auto" : ffmpeg.av_get_pix_fmt_name(f),
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
            if (index < 0 && currentValue != AVPixelFormat.AV_PIX_FMT_NONE)
            {
                // 非対応フォーマットが選択されていたらAutoにリセット
                _property.SetValue(AVPixelFormat.AV_PIX_FMT_NONE);
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
            _currentFormats = [AVPixelFormat.AV_PIX_FMT_NONE];
            _currentItems = [new EnumItem("Auto", null, AVPixelFormat.AV_PIX_FMT_NONE)];
            _selectedIndex.Value = 0;

            if (_editorRef?.TryGetTarget(out var editor) == true)
            {
                editor.Items = _currentItems;
            }
        }
    }

    private static AVPixelFormat[] GetCodecFormats(FFmpegVideoEncoderSettings settings)
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
            codec = MediaCodec.FindEncoder(outFormat.VideoCodec);
        }
        else
        {
            codec = MediaCodec.FindEncoder(settings.Codec.Name);
        }

        var fmts = codec.GetPixelFmts()
            .Where(f => ffmpeg.sws_isSupportedOutput(f) != 0)
            .ToArray();

        return fmts.Length > 0 ? fmts : GetAllFormats();
    }

    private static AVPixelFormat[] GetAllFormats()
    {
        return Enum.GetValues<AVPixelFormat>()
            .Where(f => f != AVPixelFormat.AV_PIX_FMT_NONE
                        && f >= 0
                        && ffmpeg.sws_isSupportedOutput(f) != 0)
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
