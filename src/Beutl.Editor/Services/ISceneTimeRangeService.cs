using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Edits the <see cref="Scene.Start"/> / <see cref="Scene.Duration"/> range.
/// <para>
/// <see cref="SetStart"/> / <see cref="SetEnd"/> are one-shot mutate+commit for
/// keyboard / menu callers. <see cref="UpdateStartDrag"/> / <see cref="UpdateEndDrag"/>
/// mutate per pointer frame without committing; finalize with
/// <see cref="CommitStartChange"/> / <see cref="CommitEndChange"/>. To abandon a
/// drag (pointer-exit, ESC), re-call UpdateXxxDrag with the initial values rather
/// than committing.
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
