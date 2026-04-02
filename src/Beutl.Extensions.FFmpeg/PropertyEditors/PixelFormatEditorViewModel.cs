using System.Reactive.Disposables;
using System.Text.Json.Nodes;

using Avalonia;
using Avalonia.Interactivity;

using Beutl.Controls.PropertyEditors;
using Beutl.Extensibility;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol.Messages;
#if FFMPEG_OUT_OF_PROCESS
using Beutl.FFmpegIpc.Protocol;
#else
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;
#endif
using Beutl.PropertyAdapters;
using Beutl.Reactive;
using Reactive.Bindings;
using Beutl.Extensions.FFmpeg.Encoding;

namespace Beutl.Extensions.FFmpeg.PropertyEditors;

internal sealed class PixelFormatEditorViewModel : IPropertyEditorContext
{
    private readonly IPropertyAdapter<int> _property;
    private readonly CompositeDisposable _disposables = [];
    private readonly ReactivePropertySlim<int> _selectedIndex;

    private FFmpegVideoEncoderSettings? _settings;
    private int[] _currentFormats = [];
    private IReadOnlyList<EnumItem> _currentItems = [];
    private WeakReference<EnumEditor>? _editorRef;

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
            PixelFormatInfo[] formatInfos = GetCodecFormatInfos(_settings);

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
        catch
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
    }

    private static PixelFormatInfo[] GetCodecFormatInfos(FFmpegVideoEncoderSettings? settings)
    {
        if (settings == null) return [];

        try
        {
#if FFMPEG_OUT_OF_PROCESS
            var connection = FFmpegWorkerProcess.DecodingInstance.EnsureStartedAsync().GetAwaiter().GetResult();
            var response = connection.RequestAsync<QueryPixelFormatsRequest, QueryPixelFormatsResponse>(
                MessageType.QueryPixelFormats, MessageType.QueryPixelFormatsResult,
                new QueryPixelFormatsRequest
                {
                    CodecName = settings.Codec.Equals(CodecRecord.Default) ? null : settings.Codec.Name,
                    OutputFile = settings.OutputFile
                }).AsTask().GetAwaiter().GetResult();
            return response.Formats;
#else
            MediaCodec codec;
            if (settings.Codec.Equals(CodecRecord.Default))
            {
                string? outputFile = settings.OutputFile;
                if (string.IsNullOrEmpty(outputFile))
                    return [];

                var outFormat = OutputFormat.GuessFormat(null, outputFile, null);
                codec = MediaCodec.FindEncoder(outFormat.VideoCodec);
            }
            else
            {
                codec = MediaCodec.FindEncoder(settings.Codec.Name);
            }

            return codec.GetPixelFmts()
                .Where(f => ffmpeg.sws_isSupportedOutput(f) != 0)
                .Select(f => new PixelFormatInfo { Value = (int)f, Name = ffmpeg.av_get_pix_fmt_name(f) })
                .ToArray();
#endif
        }
        catch
        {
            return [];
        }
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
