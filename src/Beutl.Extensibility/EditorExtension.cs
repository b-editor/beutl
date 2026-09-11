using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FluentAvalonia.UI.Controls;

namespace Beutl.Extensibility;

// ファイルのエディタを追加
public abstract class EditorExtension : ViewExtension
{
    public abstract FilePickerFileType GetFilePickerFileType();

    /// <summary>Gets the editor icon using FluentAvalonia 3's icon source contract.</summary>
    /// <remarks>
    /// The Avalonia 12 host requires extensions to be rebuilt against the matching Beutl SDK.
    /// Overrides compiled with FluentAvalonia 2's IconSource return type are not binary compatible.
    /// See docs/extension-authoring/avalonia-12-migration.md for the migration steps.
    /// </remarks>
    public abstract FAIconSource? GetIcon();

    public abstract bool TryCreateEditor(
        CoreObject obj,
        [NotNullWhen(true)] out Control? editor);

    /// <summary>
    /// Creates the editor context for <paramref name="obj"/>.
    /// </summary>
    /// <param name="obj">The object to open in the editor.</param>
    /// <param name="services">
    /// Host services the created context may need — e.g. <see cref="IEditorContextServices.ExtensionProvider"/>
    /// for querying other extensions. Owned by the composition root and passed in explicitly. An
    /// extension that needs nothing from the host may ignore it.
    /// </param>
    /// <param name="context">The created editor context, set when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if a context was created for <paramref name="obj"/>.</returns>
    /// <remarks>
    /// When a ProjectItem is needed here, obtain it from the ProjectItemContainer.
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
