using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// One-shot apply of the Scene-level metadata from the Scene Settings tab
/// (frame size, start, duration). Key behavior: change detection
/// (skip commit when nothing differs) and atomicity (writes collapse into one entry).
/// </summary>
public interface ISceneSettingsService
{
    /// <summary>Applies <paramref name="frameSize"/>, <paramref name="start"/>,
    /// and <paramref name="duration"/> to <paramref name="scene"/>. Returns true and
    /// commits one <c>ChangeSceneSettings</c> when at least one field differs;
    /// otherwise false and no commit.</summary>
    bool Apply(Scene scene, PixelSize frameSize, TimeSpan start, TimeSpan duration);
}
