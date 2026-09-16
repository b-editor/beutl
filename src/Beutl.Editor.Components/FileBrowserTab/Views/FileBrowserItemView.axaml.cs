using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.Editor.Components.FileBrowserTab.Views;

public sealed partial class FileBrowserItemView : UserControl
{
    public static readonly StyledProperty<string?> ItemNameProperty =
        AvaloniaProperty.Register<FileBrowserItemView, string?>(nameof(ItemName));
    public static readonly StyledProperty<Icon> IconProperty =
        AvaloniaProperty.Register<FileBrowserItemView, Icon>(nameof(Icon), Icon.Document);
    public static readonly StyledProperty<IImage?> ThumbnailProperty =
        AvaloniaProperty.Register<FileBrowserItemView, IImage?>(nameof(Thumbnail));
    public static readonly StyledProperty<bool> IsIconViewProperty =
        AvaloniaProperty.Register<FileBrowserItemView, bool>(nameof(IsIconView));

    public FileBrowserItemView() => InitializeComponent();

    public string? ItemName { get => GetValue(ItemNameProperty); set => SetValue(ItemNameProperty, value); }
    public Icon Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public IImage? Thumbnail { get => GetValue(ThumbnailProperty); set => SetValue(ThumbnailProperty, value); }
    public bool IsIconView { get => GetValue(IsIconViewProperty); set => SetValue(IsIconViewProperty, value); }
}
