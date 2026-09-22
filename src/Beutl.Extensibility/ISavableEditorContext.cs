namespace Beutl.Extensibility;

/// <summary>
/// An optional capability implemented by editor contexts that can persist their document.
/// </summary>
public interface ISavableEditorContext : IEditorContext
{
    /// <summary>
    /// Saves the document. The host calls this on the UI thread and coordinates project file writes.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> only when the save succeeds; <see langword="false"/> when it
    /// cannot be completed. Exceptions also propagate to the host as save failures.
    /// </returns>
    ValueTask<bool> SaveAsync();
}
