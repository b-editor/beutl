using Beutl.Editor.Services;

namespace Beutl.Editor.Components.LibraryTab.ViewModels;

/// <summary>
/// One node of the materials tree: a package group when <see cref="FilePath"/> is null,
/// a draggable file otherwise.
/// </summary>
public sealed class MaterialItemViewModel
{
    public required string DisplayName { get; init; }

    public string? FilePath { get; init; }

    public string? Description { get; init; }

    public InstalledMaterialKind Kind { get; init; }

    public List<MaterialItemViewModel> Children { get; } = [];

    public bool CanDragDrop => FilePath != null;
}
