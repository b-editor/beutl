using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Edits the <see cref="Scene.Start"/> / <see cref="Scene.Duration"/> range.
/// <para>
/// <see cref="SetStart"/> / <see cref="SetEnd"/> are one-shot mutate+commit for
/// keyboard / menu callers. <see cref="UpdateStartDrag"/> / <see cref="UpdateEndDrag"/>
/// mutate per pointer frame without committing; a drag calls them inside its edit
/// session, which commits or reverts them.
/// </para>
/// </summary>
public interface ISceneTimeRangeService
{
    void SetStart(Scene scene, MediaTime newStart);

    void SetEnd(Scene scene, MediaTime newEnd);

    void UpdateStartDrag(Scene scene, MediaTime pointerTime, MediaTime initialStart, MediaTime initialDuration);

    void UpdateEndDrag(Scene scene, MediaTime pointerTime);
}
