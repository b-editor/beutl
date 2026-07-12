using Avalonia;
using Avalonia.Controls;
using Avalonia.Xaml.Interactivity;

using FluentAvalonia.UI.Controls;

namespace Beutl.Controls;

public class NavItemHelper : Behavior<FANavigationViewItem>
{
    public static readonly StyledProperty<FAIconSource> RegularIconProperty
        = AvaloniaProperty.Register<NavItemHelper, FAIconSource>("RegularIcon");

    public static readonly StyledProperty<FAIconSource> FilledIconProperty
        = AvaloniaProperty.Register<NavItemHelper, FAIconSource>("FilledIcon");
    private IDisposable _disposable;
    private FAIconSourceElement _regular;
    private FAIconSourceElement _filled;

    public FAIconSource RegularIcon
    {
        get => GetValue(RegularIconProperty);
        set => SetValue(RegularIconProperty, value);
    }

    public FAIconSource FilledIcon
    {
        get => GetValue(FilledIconProperty);
        set => SetValue(FilledIconProperty, value);
    }

    protected override void OnAttached()
    {
        base.OnAttached();
        SetFontSize(RegularIcon);
        SetFontSize(FilledIcon);
        _disposable = AssociatedObject.GetPropertyChangedObservable(ListBoxItem.IsSelectedProperty)
            .Subscribe(e => SelectionChanged((FANavigationViewItem)e.Sender));

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
                SetFontSize(RegularIcon);
                _regular.IconSource = RegularIcon;
            }

            if (_filled != null)
            {
                SetFontSize(FilledIcon);
                _filled.IconSource = FilledIcon;
            }
        }
    }

    private static void SetFontSize(FAIconSource iconSource)
    {
        if (iconSource is FAFontIconSource fontIcon)
        {
            fontIcon.FontSize = 48;
        }
        else if (iconSource is FASymbolIconSource symbolIcon)
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
