using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// One-shot apply of the Scene-level metadata edited in the Scene Settings
/// tab (frame size, start, duration). The testable parts are: change
/// detection (skip commit when nothing differs) and atomicity (three
/// field writes collapse into one history entry).
/// </summary>
public interface ISceneSettingsService
{
    /// <summary>Applies <paramref name="frameSize"/>, <paramref name="start"/>,
    /// and <paramref name="duration"/> to <paramref name="scene"/>. Returns
    /// <see langword="true"/> and commits one <c>ChangeSceneSettings</c>
    /// entry when at least one field differs from the current state; returns
    /// <see langword="false"/> and commits nothing when all three already
    /// match.</summary>
    bool Apply(Scene scene, PixelSize frameSize, TimeSpan start, TimeSpan duration);
}
