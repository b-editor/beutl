using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;

using FluentAvalonia.UI.Controls;

using FAPathIcon = FluentAvalonia.UI.Controls.PathIcon;

namespace BeUtl.Controls;

public class NavItemHelper : Behavior<NavigationViewItem>
{
    public static readonly StyledProperty<Geometry> RegularIconProperty
        = AvaloniaProperty.Register<NavItemHelper, Geometry>("RegularIcon");

    public static readonly StyledProperty<Geometry> FilledIconProperty
        = AvaloniaProperty.Register<NavItemHelper, Geometry>("FilledIcon");
    private IDisposable _disposable;

    public Geometry RegularIcon
    {
        get => GetValue(RegularIconProperty);
        set => SetValue(RegularIconProperty, value);
    }

    public Geometry FilledIcon
    {
        get => GetValue(FilledIconProperty);
        set => SetValue(FilledIconProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        _disposable = AssociatedObject.GetPropertyChangedObservable(ListBoxItem.IsSelectedProperty)
            .Subscribe(e => SelectionChanged((NavigationViewItem)e.Sender));

        SelectionChanged(AssociatedObject);
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        _disposable?.Dispose();
    }

    private void SelectionChanged(NavigationViewItem sender)
    {
        FAPathIcon icon = sender.Icon as FAPathIcon ?? new FAPathIcon();
        if (sender.IsSelected)
        {
            icon.Data = FilledIcon ?? RegularIcon;
        }
        else
        {
            icon.Data = RegularIcon ?? FilledIcon;
        }

        sender.Icon = icon;
    }
}
