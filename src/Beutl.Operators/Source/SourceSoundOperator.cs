using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Beutl.Audio;
using Beutl.Audio.Composing;
using Beutl.Audio.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Operation;
using Beutl.Threading;

namespace Beutl.Operators.Source;

[Display(Name = nameof(Strings.Sound), ResourceType = typeof(Strings))]
public sealed class SourceSoundOperator : PublishOperator<SourceSound>, IElementThumbnailsProvider
{
    private EventHandler? _handler;

    public ElementThumbnailsKind ThumbnailsKind => ElementThumbnailsKind.Audio;

    public event EventHandler? ThumbnailsInvalidated;

    public override bool HasOriginalLength()
    {
        return Value?.Source.CurrentValue != null;
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
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (Value is not { } value) return;

        _handler = (_, _) => ThumbnailsInvalidated?.Invoke(this, EventArgs.Empty);
        value.Edited += _handler;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        using var resource = Value.Source.CurrentValue?.ToResource(RenderContext.Default);
        if (resource != null)
        {
            timeSpan = resource.Duration;
            return true;
        }
        else
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }
    }

    public override void OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        if (Value is null) return;

        if (backward)
        {
            Value.OffsetPosition.CurrentValue += startDelta;
        }
        else
        {
            base.OnSplit(backward, startDelta, lengthDelta);
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
        using var resource = Value.Source.CurrentValue?.ToResource(RenderContext.Default);
        if (resource == null)
            yield break;

        if (chunkCount <= 0 || samplesPerChunk <= 0)
            yield break;

        var duration = resource.Duration;
        if (duration <= TimeSpan.Zero)
            yield break;

        int sampleRate = resource.SampleRate;
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

            var chunk = await ComposeThread.Dispatcher.InvokeAsync(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return (WaveformChunk?)null;

                using var buffer = composer.Compose(new TimeRange(startTime, durationTime));
                if (buffer == null || buffer.SampleCount == 0)
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
