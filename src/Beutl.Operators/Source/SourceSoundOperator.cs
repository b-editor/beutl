using System.Runtime.CompilerServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Threading;

namespace Beutl.Operators.Source;

public sealed class SourceSoundOperator : PublishOperator<SourceSound>, IElementPreviewProvider
{
    private Uri? _uri;
    private EventHandler? _handler;

    public ElementPreviewKind PreviewKind => ElementPreviewKind.Audio;

    public event EventHandler? PreviewInvalidated;

    public override bool HasOriginalLength()
    {
        return Value?.Source.CurrentValue?.IsDisposed == false;
    }

    protected override void FillProperties()
    {
        AddProperty(Value.Source);
        AddProperty(Value.OffsetPosition, TimeSpan.Zero);
        AddProperty(Value.Gain, 100f);
        AddProperty(Value.Speed, 100f);
        AddProperty(Value.Effect, new AudioEffectGroup());
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);

        if (Value is { } value && _handler != null)
        {
            value.Edited -= _handler;
        }

        _handler = null;

        if (Value is not { Source.CurrentValue: { Uri: { } uri } source } v) return;

        _uri = uri;
        v.Source.CurrentValue = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (Value is not { } value) return;

        _handler = (_, _) => PreviewInvalidated?.Invoke(this, EventArgs.Empty);
        value.Edited += _handler;

        if (_uri is null) return;

        if (SoundSource.TryOpen(_uri, out SoundSource? source))
        {
            value.Source.CurrentValue = source;
        }
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        if (Value?.Source.CurrentValue?.IsDisposed == false)
        {
            timeSpan = Value.Source.CurrentValue.Duration;
            return true;
        }
        else
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }
    }

    public override IRecordableCommand? OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        if (Value is null) return null;

        if (backward)
        {
            TimeSpan newValue = Value.OffsetPosition.CurrentValue + startDelta;
            TimeSpan oldValue = Value.OffsetPosition.CurrentValue;

            return RecordableCommands.Create([this])
                .OnDo(() => Value.OffsetPosition.CurrentValue = newValue)
                .OnUndo(() => Value.OffsetPosition.CurrentValue = oldValue)
                .ToCommand();
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }

    public async IAsyncEnumerable<(int Index, int Count, IBitmap Thumbnail)> GetThumbnailStripAsync(
        int maxWidth,
        int maxHeight,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<WaveformChunk> GetWaveformChunksAsync(
        int chunkCount,
        int samplesPerChunk,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Value?.Source.CurrentValue is not { IsDisposed: false } source)
            yield break;

        if (chunkCount <= 0 || samplesPerChunk <= 0)
            yield break;

        var duration = source.Duration;
        if (duration <= TimeSpan.Zero)
            yield break;

        int sampleRate = source.SampleRate;
        int totalSamples = (int)(duration.TotalSeconds * sampleRate);

        using var composer = new Composer { SampleRate = sampleRate };
        composer.AddSound(Value);

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            int startSample = (int)((long)chunkIndex * totalSamples / chunkCount);
            int endSample = (int)((long)(chunkIndex + 1) * totalSamples / chunkCount);
            int sampleCount = Math.Min(endSample - startSample, samplesPerChunk);
            TimeSpan startTime = Value.TimeRange.Start + TimeSpan.FromSeconds((double)startSample / sampleRate);
            TimeSpan durationTime = TimeSpan.FromSeconds((double)sampleCount / sampleRate);

            if (sampleCount <= 0)
                continue;

            var chunk = await RenderThread.Dispatcher.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return (WaveformChunk?)null;

                using var buffer = composer.Compose(new TimeRange(startTime, durationTime));
                if (buffer == null)
                    return null;

                var firstChannel = buffer.GetChannelData(0);
                var secondChannel = buffer.GetChannelData(1);

                float minValue = float.MaxValue;
                float maxValue = float.MinValue;

                for (int i = 0; i < buffer.SampleCount; i++)
                {
                    float left = firstChannel[i];
                    float right = secondChannel[i];

                    float monoValue = (left + right) * 0.5f;
                    minValue = Math.Min(minValue, monoValue);
                    maxValue = Math.Max(maxValue, monoValue);
                }

                return new WaveformChunk(chunkIndex, minValue, maxValue);
            }, DispatchPriority.Low, cancellationToken);

            if (chunk.HasValue)
            {
                yield return chunk.Value;
            }
        }
    }
}
