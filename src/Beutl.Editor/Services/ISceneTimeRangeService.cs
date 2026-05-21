using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Edits the <see cref="Scene.Start"/> and <see cref="Scene.Duration"/> range.
/// <para>
/// <see cref="SetStart"/> / <see cref="SetEnd"/> are one-shot mutate+commit
/// methods for keyboard / menu callers. <see cref="UpdateStartDrag"/> and
/// <see cref="UpdateEndDrag"/> mutate the scene without committing — call them
/// per pointer frame during a drag, then finalize the history entry with
/// <see cref="CommitStartChange"/> / <see cref="CommitEndChange"/>. Callers
/// that abandon a drag (pointer-exit, ESC) re-call the matching UpdateXxxDrag
/// with the initial values to restore state instead of committing.
/// </para>
/// </summary>
public interface ISceneTimeRangeService
{
    void SetStart(Scene scene, TimeSpan newStart);

    void SetEnd(Scene scene, TimeSpan newEnd);

    void UpdateStartDrag(Scene scene, TimeSpan pointerTime, TimeSpan initialStart, TimeSpan initialDuration);

    void UpdateEndDrag(Scene scene, TimeSpan pointerTime);

    void CommitStartChange();

    void CommitEndChange();
}
