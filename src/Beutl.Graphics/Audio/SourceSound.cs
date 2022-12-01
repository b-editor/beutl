using System.Text.Json.Nodes;

using Beutl.Media;
using Beutl.Media.Music;
using Beutl.Media.Source;

namespace Beutl.Audio;

public sealed class SourceSound : Sound
{
    public static readonly CoreProperty<ISoundSource?> SourceProperty;
    private ISoundSource? _source;
    private string? _sourceName;

    static SourceSound()
    {
        SourceProperty = ConfigureProperty<ISoundSource?, SourceSound>(nameof(Source))
            .Accessor(o => o.Source, (o, v) => o.Source = v)
            .PropertyFlags(PropertyFlags.All & ~PropertyFlags.Animatable)
            .DefaultValue(null)
            .Register();

        AffectsRender<SourceSound>(SourceProperty);
    }

    public ISoundSource? Source
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
            && MediaSourceManager.Shared.OpenSoundSource(_sourceName, out ISoundSource? soundSource))
        {
            Source = soundSource;
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

    protected override void OnRecord(IAudio audio, TimeRange range)
    {
        if (Source?.IsDisposed == false
            && Source.Read(range.Start, range.Duration, out IPcm? pcm))
        {
            audio.RecordPcm(pcm);
        }
    }

    protected override TimeSpan TimeCore(TimeSpan available)
    {
        if (Source != null)
        {
            return Source.Duration;
        }
        else
        {
            return available;
        }
    }
}
