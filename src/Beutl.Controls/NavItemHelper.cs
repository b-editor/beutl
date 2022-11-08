using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Xaml.Interactivity;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

public class NavItemHelper : Behavior<NavigationViewItem>
{
    public static readonly StyledProperty<Geometry> RegularIconProperty
        = AvaloniaProperty.Register<NavItemHelper, Geometry>("RegularIcon");

    public static readonly StyledProperty<Geometry> FilledIconProperty
        = AvaloniaProperty.Register<NavItemHelper, Geometry>("FilledIcon");
    private IDisposable _disposable;
    private FAPathIcon _regular;
    private FAPathIcon _filled;

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
        _regular = new FAPathIcon()
        {
            Data = RegularIcon
        };
        _filled = new FAPathIcon()
        {
            Data = FilledIcon
        };
        _disposable = AssociatedObject.GetPropertyChangedObservable(ListBoxItem.IsSelectedProperty)
            .Subscribe(e => SelectionChanged((NavigationViewItem)e.Sender));

        SelectionChanged(AssociatedObject);
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        _regular = null;
        _filled = null;
        _disposable?.Dispose();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property.Name is nameof(RegularIcon) or nameof(FilledIcon))
        {
            if (_regular != null)
            {
                _regular.Data = RegularIcon;
            }

            if (_filled != null)
            {
                _filled.Data = FilledIcon;
            }
        }
    }

    private void SelectionChanged(NavigationViewItem sender)
    {
        if (sender.IsSelected)
        {
            sender.Icon = _filled;
        }
        else
        {
            sender.Icon = _regular;
        }
    }
}
