using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.Editor.Services;

/// <summary>
/// Parses clipboard JSON payloads for <see cref="KeyFrameAnimation"/> and
/// <see cref="KeyFrame"/> and applies them to a target animation. The
/// type-discriminator validation, generic-mismatch fallback, and
/// existing-key collision logic was previously duplicated between
/// <c>GraphEditorViewModel</c> and <c>InlineAnimationLayerViewModel</c>.
/// </summary>
public interface IKeyFrameClipboardService
{
    /// <summary>Parses <paramref name="json"/> as a <see cref="KeyFrameAnimation"/>
    /// and overwrites <paramref name="target"/>. On success commits one
    /// <c>PasteAnimation</c> entry.</summary>
    KeyFrameAnimationPasteOutcome PasteAnimation(KeyFrameAnimation target, string json);

    /// <summary>Parses <paramref name="json"/> as a single <see cref="KeyFrame"/>
    /// and inserts (or replaces) it on <paramref name="target"/> at
    /// <paramref name="keyTime"/>. On success commits one <c>PasteKeyFrame</c>
    /// entry. When the pasted key's generic type does not match
    /// <paramref name="target"/>, returns
    /// <see cref="KeyFramePasteOutcome.GenericTypeMismatch"/> with the parsed
    /// easing — the caller is expected to fall back to its own
    /// <c>InsertKeyFrame</c> path using that easing.</summary>
    KeyFramePasteResult PasteKeyFrame(KeyFrameAnimation target, string json, TimeSpan keyTime);
}

public enum KeyFrameAnimationPasteOutcome
{
    InvalidJson,
    MissingType,
    TypeIsNotKeyFrameAnimation,
    GenericTypeMismatch,
    UnexpectedError,
    Pasted,
}

public enum KeyFramePasteOutcome
{
    InvalidJson,
    MissingType,
    TypeIsNotKeyFrame,
    GenericTypeMismatch,
    UnexpectedError,
    ReplacedExisting,
    Inserted,
}

public sealed record KeyFramePasteResult(KeyFramePasteOutcome Outcome, Easing? EasingForFallback = null);
