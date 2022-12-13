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
            .SerializeName("source")
            .JsonConverter(new SoundSourceJsonConverter())
            .Register();

        AffectsRender<SourceSound>(SourceProperty);
    }

    public ISoundSource? Source
    {
        get => _source;
        set => SetAndRaise(SourceProperty, ref _source, value);
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
            _sourceName = null;
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
            pcm.Dispose();
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
