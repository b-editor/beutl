using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class KeyFrameClipboardService : IKeyFrameClipboardService
{
    private static readonly ILogger s_logger = Log.CreateLogger<KeyFrameClipboardService>();

    private readonly HistoryManager _historyManager;

    public KeyFrameClipboardService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public KeyFrameAnimationPasteOutcome PasteAnimation(KeyFrameAnimation target, string json)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(json);

        if (ClipboardJson.TryParse(json) is not JsonObject newJson)
        {
            return KeyFrameAnimationPasteOutcome.InvalidJson;
        }

        if (!newJson.TryGetDiscriminator(out Type? discriminator))
        {
            return KeyFrameAnimationPasteOutcome.MissingType;
        }

        if (!discriminator.IsAssignableTo(typeof(IKeyFrameAnimation)))
        {
            return KeyFrameAnimationPasteOutcome.TypeIsNotKeyFrameAnimation;
        }

        if (discriminator.GenericTypeArguments.Length == 0
            || discriminator.GenericTypeArguments[0] != target.ValueType)
        {
            return KeyFrameAnimationPasteOutcome.GenericTypeMismatch;
        }

        try
        {
            // Preserve the target's identity (Id) so observers tracking the
            // animation by id keep their subscriptions valid across paste.
            // New ids are minted for the keyframes so the pasted set does not
            // alias the source.
            Guid id = target.Id;
            CoreSerializer.PopulateFromJsonObject(target, newJson);
            target.Id = id;
            foreach (IKeyFrame item in target.KeyFrames)
            {
                item.Id = Guid.NewGuid();
            }

            _historyManager.Commit(CommandNames.PasteAnimation);
            return KeyFrameAnimationPasteOutcome.Pasted;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "PasteAnimation failed while populating target animation from JSON.");
            return KeyFrameAnimationPasteOutcome.UnexpectedError;
        }
    }

    public KeyFramePasteResult PasteKeyFrame(KeyFrameAnimation target, string json, TimeSpan keyTime)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(json);

        if (ClipboardJson.TryParse(json) is not JsonObject newJson)
        {
            return new KeyFramePasteResult(KeyFramePasteOutcome.InvalidJson);
        }

        if (!newJson.TryGetDiscriminator(out Type? discriminator))
        {
            return new KeyFramePasteResult(KeyFramePasteOutcome.MissingType);
        }

        if (!discriminator.IsAssignableTo(typeof(KeyFrame)))
        {
            return new KeyFramePasteResult(KeyFramePasteOutcome.TypeIsNotKeyFrame);
        }

        KeyFrame newKeyFrame;
        try
        {
            newKeyFrame = (KeyFrame)Activator.CreateInstance(discriminator)!;
            CoreSerializer.PopulateFromJsonObject(newKeyFrame, newJson);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "PasteKeyFrame failed while parsing key-frame JSON.");
            return new KeyFramePasteResult(KeyFramePasteOutcome.UnexpectedError);
        }

        if (discriminator.GenericTypeArguments.Length == 0
            || discriminator.GenericTypeArguments[0] != target.ValueType)
        {
            // Caller handles the fallback "insert with this easing at keyTime"
            // because creating a new key requires the View's property-value
            // accessor — the service does not know the typed value.
            return new KeyFramePasteResult(
                KeyFramePasteOutcome.GenericTypeMismatch,
                EasingForFallback: newKeyFrame.Easing);
        }

        try
        {
            if (target.KeyFrames.FirstOrDefault(k => k.KeyTime == keyTime) is { } existing)
            {
                existing.Easing = newKeyFrame.Easing;
                existing.Value = ((IKeyFrame)newKeyFrame).Value;
                _historyManager.Commit(CommandNames.PasteKeyFrame);
                return new KeyFramePasteResult(KeyFramePasteOutcome.ReplacedExisting);
            }

            newKeyFrame.KeyTime = keyTime;
            target.KeyFrames.Add((IKeyFrame)newKeyFrame, out _);
            _historyManager.Commit(CommandNames.PasteKeyFrame);
            return new KeyFramePasteResult(KeyFramePasteOutcome.Inserted);
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "PasteKeyFrame failed while applying key-frame to target.");
            return new KeyFramePasteResult(KeyFramePasteOutcome.UnexpectedError);
        }
    }
}
