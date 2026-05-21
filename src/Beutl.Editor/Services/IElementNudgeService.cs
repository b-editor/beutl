using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>
/// Coalesces a burst of keyboard nudges into a single history commit. Calls
/// to <see cref="Nudge"/> mutate the targeted elements immediately and start
/// (or reset) a debounce window; <see cref="Flush"/> drains the pending
/// commit synchronously, e.g. when the user starts an unrelated mutation.
/// </summary>
public interface IElementNudgeService : IDisposable
{
    void Nudge(Scene scene, IReadOnlyList<Element> targets, int frames);

    void Flush();
}
