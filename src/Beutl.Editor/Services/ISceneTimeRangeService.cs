using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Edits the <see cref="Scene.Start"/> and <see cref="Scene.Duration"/> range.
/// Drag interactions are expressed as a <see cref="ISceneTimeRangeDragSession"/>:
/// the session mutates the scene without committing while the user drags, and
/// finalizes a single history entry on <see cref="ISceneTimeRangeDragSession.Commit"/>.
/// </summary>
public interface ISceneTimeRangeService
{
    void SetStart(Scene scene, TimeSpan newStart);

    void SetEnd(Scene scene, TimeSpan newEnd);

    ISceneTimeRangeDragSession BeginDragStart(Scene scene);

    ISceneTimeRangeDragSession BeginDragEnd(Scene scene);
}

public interface ISceneTimeRangeDragSession : IDisposable
{
    void Update(TimeSpan pointerFrame);

    void Commit();

    void Cancel();
}
