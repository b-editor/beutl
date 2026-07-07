using Beutl.Audio;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Splits one or more <see cref="Element"/>s at silence-region boundaries, owning the
/// history commit. <see cref="SplitBySilence"/> runs the whole batch (split of every
/// target at every boundary, plus the optional delete of the silence pieces) and commits
/// one undo entry only when the scene actually changes. Ripple-shift of the surviving
/// pieces is not performed here — it is deferred pending the dedicated Ripple feature.
/// </summary>
public interface ISilenceSplitService
{
    /// <summary>
    /// Splits <paramref name="targets"/> at the boundaries of <paramref name="silenceRegions"/>.
    /// The regions are in <b>scene-timeline coordinates</b> (the same space as
    /// <see cref="Element.Start"/>), not element-local — the caller offsets a detector's 0-based
    /// <see cref="SilenceRegion"/>s by each source's timeline start before calling this.
    /// </summary>
    SilenceSplitOutcome SplitBySilence(
        Scene scene,
        IReadOnlyList<Element> targets,
        IReadOnlyList<SilenceRegion> silenceRegions,
        SilenceSplitMode mode);
}

public enum SilenceSplitMode
{
    /// <summary>Split each target at every silence boundary; no pieces are deleted.</summary>
    SplitOnly = 0,

    /// <summary>Split, then delete the pieces whose range lies inside a silence region.</summary>
    SplitAndDeleteSilence = 1,
}

public sealed record SilenceSplitOutcome(int SplitCount, int DeletedCount)
{
    public static readonly SilenceSplitOutcome None = new(0, 0);
}
