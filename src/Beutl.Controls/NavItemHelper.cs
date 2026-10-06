using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

using FluentAvalonia.UI.Controls;

using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;

namespace Beutl.Controls;

public class NavItemHelper : Behavior<FANavigationViewItem>
{
    public static readonly StyledProperty<FAIconSource?> RegularIconProperty
        = AvaloniaProperty.Register<NavItemHelper, FAIconSource?>("RegularIcon");

    public static readonly StyledProperty<FAIconSource?> FilledIconProperty
        = AvaloniaProperty.Register<NavItemHelper, FAIconSource?>("FilledIcon");
    private IDisposable? _disposable;

    public FAIconSource? RegularIcon
    {
        get => GetValue(RegularIconProperty);
        set => SetValue(RegularIconProperty, value);
    }

    public FAIconSource? FilledIcon
    {
        get => GetValue(FilledIconProperty);
        set => SetValue(FilledIconProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        if (AssociatedObject is not { } item) return;
        SetFontSize(RegularIcon);
        SetFontSize(FilledIcon);
        _disposable = item.GetPropertyChangedObservable(ListBoxItem.IsSelectedProperty)
            .Subscribe(e => SelectionChanged((FANavigationViewItem)e.Sender));

        SelectionChanged(item);
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        _disposable?.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        // The override stays because it is part of the public API.
        base.OnPropertyChanged(change);
    }

    private static void SetFontSize(FAIconSource? iconSource)
    {
        if (iconSource is FAFontIconSource fontIcon)
        {
            fontIcon.FontSize = 48;
        }
        else if (iconSource is FluentIconSource symbolIcon)
        {
            symbolIcon.FontSize = 48;
        }
    }

    private void SelectionChanged(FANavigationViewItem sender)
    {
        if (sender.IsSelected)
        {
            sender.IconSource = FilledIcon;
        }
        else
        {
            sender.IconSource = RegularIcon;
        }
    }
}
