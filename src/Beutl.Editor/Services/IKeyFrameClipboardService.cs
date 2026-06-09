using Beutl.Animation;
using Beutl.Animation.Easings;

namespace Beutl.Editor.Services;

/// <summary>
/// Parses clipboard JSON for <see cref="KeyFrameAnimation"/> / <see cref="KeyFrame"/>
/// and applies it to a target animation. Centralizes the type-discriminator
/// validation, generic-mismatch fallback, and existing-key collision logic that
/// was duplicated between the graph and inline-animation ViewModels.
/// </summary>
public interface IKeyFrameClipboardService
{
    /// <summary>Parses <paramref name="json"/> as a <see cref="KeyFrameAnimation"/>
    /// and overwrites <paramref name="target"/>. On success commits one
    /// <c>PasteAnimation</c> entry.</summary>
    KeyFrameAnimationPasteOutcome PasteAnimation(KeyFrameAnimation target, string json);

    /// <summary>Parses <paramref name="json"/> as a single <see cref="KeyFrame"/>
    /// and inserts/replaces it at <paramref name="keyTime"/>, committing one
    /// <c>PasteKeyFrame</c>. On generic-type mismatch returns
    /// <see cref="KeyFramePasteOutcome.GenericTypeMismatch"/> with the parsed easing,
    /// so the caller can fall back to its own <c>InsertKeyFrame</c> using it.</summary>
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
