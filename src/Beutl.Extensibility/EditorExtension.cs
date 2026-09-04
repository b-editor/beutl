using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;

namespace Beutl.Extensibility;

// ファイルのエディタを追加
public abstract class EditorExtension : ViewExtension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    public abstract IconSource? GetIcon();

    public abstract bool TryCreateEditor(
        CoreObject obj,
        [NotNullWhen(true)] out Control? editor);

    /// <summary>
    /// Creates the editor context for <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The object to open in the editor.</param>
    /// <param name="services">
    /// Host services owned by the composition root and passed in explicitly. A successful
    /// implementation must retain <see cref="IEditorContextServices.CloseService"/> and expose it,
    /// directly or through a context-specific wrapper, through
    /// <see cref="IEditorContext.CloseService"/>. The wrapper must forward the same stable,
    /// non-null <see cref="IEditorContextCloseService.HostToken"/> so the context cannot be
    /// attached to another editor host. The extension provider is available for querying other
    /// extensions.
    /// </param>
    /// <param name="context">The created editor context, set when this returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when a new, non-null context was created and ownership is transferred
    /// to the host; otherwise <see langword="false"/> with <paramref name="context"/> set to
    /// <see langword="null"/>.
    /// </returns>
    /// <remarks>
    /// When a ProjectItem is needed here, obtain it from the ProjectItemContainer. Returning a
    /// context without the supplied close capability violates the host ownership contract. After a
    /// successful return, the host owns disposal exactly once, including when a later attachment or
    /// publication step fails. The returned context must be newly created and unowned; returning a
    /// context that is already active in a tab violates the ownership contract. On failure, the
    /// extension must dispose any partially initialized state and must not return a context.
    /// Implementations must not synchronously start and wait for a project or editor lifecycle
    /// operation, on this thread or another; enqueue that work to begin after this callback returns.
    /// </remarks>
    public abstract bool TryCreateContext(
        CoreObject obj,
        IEditorContextServices services,
        [NotNullWhen(true)] out IEditorContext? context);

    public virtual bool IsSupported(string? file)
    {
        return file != null && MatchFileExtension(Path.GetExtension(file));
    }

    // extはピリオドを含む
    public abstract bool MatchFileExtension(string ext);
}
