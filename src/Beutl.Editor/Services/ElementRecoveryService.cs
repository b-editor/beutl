using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

/// <summary>Completes element repairs as part of the caller's history transaction.</summary>
public static class ElementRecoveryService
{
    /// <summary>
    /// Rechecks recovery blockers after a repair and resumes normal persistence when none remain.
    /// Call after changing the element, before committing the same <paramref name="history"/>
    /// transaction. Undo restores the retained source bytes; this method does not commit history.
    /// </summary>
    /// <returns>Whether recovery protection was removed and recorded in history.</returns>
    public static bool TryCompleteRepair(Element element, HistoryManager history)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(history);
        if (Scene.TryResumeElementPersistence(element) is not { } suppression) return false;

        try
        {
            history.Record(
                () => element.SuppressedStorageSource = null,
                () =>
                {
                    suppression.WasReinstated = true;
                    element.SuppressedStorageSource = suppression;
                });
        }
        catch
        {
            element.SuppressedStorageSource = suppression;
            throw;
        }
        return true;
    }
}
