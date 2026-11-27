using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.FileBrowserTab.Views;

public sealed partial class FileBrowserItemView : UserControl
{
    public FileBrowserItemView() => InitializeComponent();

    [GeneratedStyledProperty]
    public partial string? ItemName { get; set; }
    [GeneratedStyledProperty(DefaultValue = Icon.Document)]
    public partial Icon Icon { get; set; }
    [GeneratedStyledProperty]
    public partial IImage? Thumbnail { get; set; }
    [GeneratedStyledProperty]
    public partial bool IsIconView { get; set; }
    [GeneratedStyledProperty]
    public partial bool IsProcessing { get; set; }
    [GeneratedStyledProperty(DefaultValue = true)]
    public partial bool IsProgressIndeterminate { get; set; }
    [GeneratedStyledProperty]
    public partial double ProgressValue { get; set; }
}
