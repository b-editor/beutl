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
    /// Asynchronously creates the editor context for <paramref name="obj"/>.
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
    /// <returns>
    /// A new context whose ownership is transferred to the host, or <see langword="null"/> after
    /// all partial initialization state has been asynchronously cleaned up.
    /// </returns>
    /// <remarks>
    /// When a ProjectItem is needed here, obtain it from the ProjectItemContainer. Returning a
    /// context without the supplied close capability violates the host ownership contract. A
    /// non-null return transfers disposal to the host exactly once, including when a later attachment or
    /// publication step fails. The returned context must be newly created and unowned; returning a
    /// context that is already active in a tab violates the ownership contract. Before returning
    /// <see langword="null"/>, the extension must await disposal of any partially initialized state.
    /// Implementations must not synchronously start and wait for a project or editor lifecycle
    /// operation, on this thread or another; enqueue that work to begin after this callback returns.
    /// </remarks>
    public abstract ValueTask<IEditorContext?> CreateContextAsync(
        CoreObject obj,
        IEditorContextServices services);

    public virtual bool IsSupported(string? file)
    {
        return file != null && MatchFileExtension(Path.GetExtension(file));
    }

    // extはピリオドを含む
    public abstract bool MatchFileExtension(string ext);
}
