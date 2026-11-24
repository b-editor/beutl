using Avalonia;
using Avalonia.Controls;

namespace Beutl.Controls;

/// <summary>
/// Tells that a view has nothing to show yet: an icon, a title and a description, with an action such as a
/// button as its content. Each part that is not set takes no space.
/// </summary>
public partial class EmptyState : ContentControl
{
    [GeneratedStyledProperty]
    public partial object? Icon { get; set; }

    [GeneratedStyledProperty]
    public partial string? Title { get; set; }

    [GeneratedStyledProperty]
    public partial string? Description { get; set; }
}
