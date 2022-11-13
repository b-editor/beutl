using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Animation;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Rendering;
using Beutl.Utilities;
using Beutl.Validation;

namespace Beutl.Graphics.Drawables;

public enum VideoPositionMode
{
    Manual,
    Automatic
}

public class VideoFrame : Drawable
{
    public static readonly CoreProperty<TimeSpan> OffsetPositionProperty;
    public static readonly CoreProperty<TimeSpan> PlaybackPositionProperty;
    public static readonly CoreProperty<VideoPositionMode> PositionModeProperty;
    public static readonly CoreProperty<FileInfo?> SourceFileProperty;
    private TimeSpan _offsetPosition;
    private TimeSpan _playbackPosition;
    private VideoPositionMode _positionMode;
    private FileInfo? _sourceFile;
    private MediaReader? _mediaReader;
    private TimeSpan _requestedPosition;
    private IBitmap? _previousBitmap;
    private double _previousFrame;
    private LayerNode? _layerNode;

    static VideoFrame()
    {
        OffsetPositionProperty = ConfigureProperty<TimeSpan, VideoFrame>(nameof(OffsetPosition))
            .Accessor(o => o.OffsetPosition, (o, v) => o.OffsetPosition = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(TimeSpan.Zero)
            .SerializeName("offset-position")
            .Register();

        PlaybackPositionProperty = ConfigureProperty<TimeSpan, VideoFrame>(nameof(PlaybackPosition))
            .Accessor(o => o.PlaybackPosition, (o, v) => o.PlaybackPosition = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(TimeSpan.Zero)
            .Minimum(TimeSpan.Zero)
            .SerializeName("playback-position")
            .Register();

        PositionModeProperty = ConfigureProperty<VideoPositionMode, VideoFrame>(nameof(PositionMode))
            .Accessor(o => o.PositionMode, (o, v) => o.PositionMode = v)
            .PropertyFlags(PropertyFlags.All)
            .DefaultValue(VideoPositionMode.Automatic)
            .SerializeName("position-mode")
            .Register();

        SourceFileProperty = ConfigureProperty<FileInfo?, VideoFrame>(nameof(SourceFile))
            .Accessor(o => o.SourceFile, (o, v) => o.SourceFile = v)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
#if DEBUG
            .Validator(new FileInfoExtensionValidator()
            {
                FileExtensions = new[] { "mp4" }
            })
#else
#warning Todo: DecoderRegistryからファイル拡張子を取得してセットする。
#endif
            .Register();

        AffectsRender<VideoFrame>(
            OffsetPositionProperty,
            PlaybackPositionProperty,
            PositionModeProperty,
            SourceFileProperty);
    }

    public TimeSpan OffsetPosition
    {
        get => _offsetPosition;
        set => SetAndRaise(OffsetPositionProperty, ref _offsetPosition, value);
    }

    public TimeSpan PlaybackPosition
    {
        get => _playbackPosition;
        set => SetAndRaise(PlaybackPositionProperty, ref _playbackPosition, value);
    }

    public VideoPositionMode PositionMode
    {
        get => _positionMode;
        set => SetAndRaise(PositionModeProperty, ref _positionMode, value);
    }

    public FileInfo? SourceFile
    {
        get => _sourceFile;
        set => SetAndRaise(SourceFileProperty, ref _sourceFile, value);
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobj
            && jobj.TryGetPropertyValue("source-file", out JsonNode? fileNode)
            && fileNode is JsonValue fileValue
            && fileValue.TryGetValue(out string? fileStr)
            && File.Exists(fileStr))
        {
            SourceFile = new FileInfo(fileStr);
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobj
            && _sourceFile != null)
        {
            jobj["source-file"] = _sourceFile.FullName;
        }
    }

    public override void ApplyAnimations(IClock clock)
    {
        base.ApplyAnimations(clock);
        if (PositionMode == VideoPositionMode.Automatic)
        {
            _requestedPosition = clock.CurrentTime;

            if (_layerNode != null)
            {
                _requestedPosition -= _layerNode.Start;
            }
        }
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        _layerNode = args.Parent?.FindLogicalParent<LayerNode>(true);
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        _layerNode = null;
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (_mediaReader?.IsDisposed == false)
        {
            return _mediaReader.VideoInfo.FrameSize.ToSize(1);
        }
        else
        {
            return Size.Empty;
        }
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (_mediaReader?.IsDisposed == false)
        {
            if (PositionMode == VideoPositionMode.Manual)
            {
                _requestedPosition = _playbackPosition;
            }

            TimeSpan pos = _requestedPosition - _offsetPosition;
            Rational rate = _mediaReader.VideoInfo.FrameRate;
            double frameNum = pos.TotalSeconds * (rate.Numerator / (double)rate.Denominator);

            if (_previousBitmap?.IsDisposed == false
                && MathUtilities.AreClose(frameNum, _previousFrame))
            {
                canvas.DrawBitmap(_previousBitmap);
            }
            else if (_mediaReader.ReadVideo((int)frameNum, out IBitmap? bmp)
                && bmp?.IsDisposed == false)
            {
                canvas.DrawBitmap(bmp);

                _previousBitmap?.Dispose();
                _previousBitmap = bmp;
                _previousFrame = frameNum;
            }
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.PropertyName is nameof(SourceFile))
        {
            _previousBitmap?.Dispose();
            _previousBitmap = null;
            _previousFrame = -1;
            _mediaReader?.Dispose();
            _mediaReader = null;

            TryOpenMediaFile();
        }
    }

    [MemberNotNullWhen(true, "_mediaReader")]
    private bool TryOpenMediaFile()
    {
        if (_sourceFile?.Exists == true)
        {
            try
            {
                if (_mediaReader?.IsDisposed != false)
                {
                    _mediaReader = MediaReader.Open(_sourceFile.FullName, new MediaOptions()
                    {
                        StreamsToLoad = MediaMode.Video
                    });

                    if (!_mediaReader.HasVideo)
                    {
                        _mediaReader.Dispose();
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
        else
        {
            return false;
        }
    }
}
