using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Pixel;
using Beutl.Media.Source;
using Beutl.Validation;

namespace Beutl.Graphics;

public class SourceImage : Drawable
{
    public static readonly CoreProperty<IImageSource?> SourceProperty;
    private IImageSource? _source;
    private string? _sourceName;

    static SourceImage()
    {
        SourceProperty = ConfigureProperty<IImageSource?, SourceImage>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
            .Register();

        AffectsRender<SourceImage>(SourceProperty);
    }

    public IImageSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobj
            && jobj.TryGetPropertyValue("source", out JsonNode? fileNode)
            && fileNode is JsonValue fileValue
            && fileValue.TryGetValue(out string? fileStr))
        {
            if (Parent != null && _sourceName != fileStr)
            {
                Close();
                _sourceName = fileStr;
                Open();
            }
            else
            {
                _sourceName = fileStr;
            }
        }
        else
        {
            _sourceName = null;
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobj
            && _source != null)
        {
            jobj["source"] = _source.Name;
        }
    }

    protected override void OnAttachedToLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnAttachedToLogicalTree(args);
        Open();
    }

    protected override void OnDetachedFromLogicalTree(in LogicalTreeAttachmentEventArgs args)
    {
        base.OnDetachedFromLogicalTree(args);
        Close();
    }

    private void Open()
    {
        if (_sourceName != null
            && MediaSourceManager.Shared.OpenImageSource(_sourceName, out IImageSource? imageSource))
        {
            Source = imageSource;
        }
    }

    private void Close()
    {
        if (Source != null)
        {
            _sourceName = Source.Name;
            Source.Dispose();
            Source = null;
        }
    }

    protected override Size MeasureCore(Size availableSize)
    {
        if (_source != null)
        {
            return _source.FrameSize.ToSize(1);
        }
        else
        {
            return default;
        }
    }

    protected override void OnDraw(ICanvas canvas)
    {
        if (_source?.Read(out IBitmap? bitmap) == true)
        {
            using (bitmap)
            {
                canvas.DrawBitmap(bitmap);
            }
        }
    }
}
