using Beutl.Controls;
using Beutl.Editor.Components.LibraryThumbnails;
using Beutl.Services;

namespace Beutl.Editor.Components.LibraryTab.ViewModels;

/// <summary>A tile of the library's effect or transition gallery: one library item and its moving thumbnail.</summary>
public sealed class LibraryGalleryItemViewModel
{
    private readonly LibraryThumbnailSet? _thumbnails;
    private IAnimatedThumbnailSource? _thumbnail;
    private bool _thumbnailResolved;

    internal LibraryGalleryItemViewModel(LibraryItemViewModel item, Type type, LibraryThumbnailSet? thumbnails)
    {
        Item = item;
        Type = type;
        _thumbnails = thumbnails;
    }

    public LibraryItemViewModel Item { get; }

    /// <summary>The type the tile adds: the item's implementation for the gallery's format.</summary>
    public Type Type { get; }

    public string DisplayName => Item.DisplayName;

    public string? Description => Item.Description;

    // Looked up when a tile first binds to it, on the UI thread.
    public IAnimatedThumbnailSource? Thumbnail
    {
        get
        {
            if (!_thumbnailResolved)
            {
                _thumbnail = _thumbnails?.GetSource(Type);
                _thumbnailResolved = true;
            }

            return _thumbnail;
        }
    }

    /// <summary>
    /// The items of <paramref name="items"/> and their groups that add a type of <paramref name="format"/>, in the
    /// library's order, reusing the tiles in <paramref name="existing"/> for the items they show.
    /// </summary>
    internal static List<LibraryGalleryItemViewModel> Collect(
        IEnumerable<LibraryItemViewModel> items,
        string format,
        LibraryThumbnailSet? thumbnails,
        IEnumerable<LibraryGalleryItemViewModel>? existing = null)
    {
        // Library item view models compare by reference, and the library keeps those of the items that stay.
        Dictionary<LibraryItemViewModel, LibraryGalleryItemViewModel> reusable =
            existing?.ToDictionary(tile => tile.Item) ?? [];
        var result = new List<LibraryGalleryItemViewModel>();
        Walk(items);
        return result;

        void Walk(IEnumerable<LibraryItemViewModel> list)
        {
            foreach (LibraryItemViewModel item in list)
            {
                Type? type = item.Data switch
                {
                    SingleTypeLibraryItem single when single.Format == format => single.ImplementationType,
                    MultipleTypeLibraryItem multiple => multiple.Types.GetValueOrDefault(format),
                    _ => null,
                };
                if (type != null)
                {
                    result.Add(reusable.TryGetValue(item, out LibraryGalleryItemViewModel? tile) && tile.Type == type
                        ? tile
                        : new LibraryGalleryItemViewModel(item, type, thumbnails));
                }

                Walk(item.Children);
            }
        }
    }
}
