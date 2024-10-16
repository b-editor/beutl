using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Beutl.ViewModels.SettingsPages;
using FluentAvalonia.Core;
using FluentAvalonia.UI.Controls;
using FluentAvalonia.UI.Controls.Primitives;
using Reactive.Bindings;

namespace Beutl.Pages.SettingsPages;

public partial class KeyMapSettingsPage : UserControl
{
    private readonly ReactiveProperty<KeyGesture?> _keyGesture = new();

    public KeyMapSettingsPage()
    {
        InitializeComponent();
    }

    private void OnItemClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: KeyMapSettingsItem item } control)
        {
            var flyout = new KeyMapFlyout();
            _keyGesture.Value = item.KeyGesture.Value;
            flyout.Bind(KeyMapFlyout.GestureProperty, _keyGesture);
            flyout.ShowAt(control);
            flyout.Confirmed += (_, _) => item.SetKeyGesture(flyout.Gesture);

            e.Handled = true;
        }
    }

    private void OnButtonKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LeftCtrl
            or Key.RightCtrl or Key.LWin or Key.RWin)
        {
            return;
        }

        _keyGesture.Value = new KeyGesture(e.Key, e.KeyModifiers);
    }
}

public sealed class KeyMapFlyout : PickerFlyoutBase
{
    public static readonly StyledProperty<KeyGesture?> GestureProperty =
        AvaloniaProperty.Register<KeyMapFlyout, KeyGesture?>(nameof(Gesture));

    public KeyGesture? Gesture
    {
        get => GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    public event TypedEventHandler<KeyMapFlyout, EventArgs>? Confirmed;

    protected override Control CreatePresenter()
    {
        var pfp = new PickerFlyoutPresenter();
        pfp.Padding = new Thickness(8);
        var textBox = new TextBox();
        textBox.Bind(TextBox.TextProperty, this.GetObservable(GestureProperty)
            .Select(i => i?.ToString() ?? "(None)"));
        textBox.IsReadOnly = true;
        var deleteButton = new Button
        {
            Content = new SymbolIcon { Symbol = Symbol.Delete },
            Theme = Application.Current?.FindResource("TransparentButton") as ControlTheme,
            Margin = new(4, 0, 0, 0)
        };
        Grid.SetColumn(deleteButton, 1);
        deleteButton.Click += (_, _) => Gesture = null;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(textBox);
        grid.Children.Add(deleteButton);
        pfp.Content = grid;

        pfp.Focusable = true;
        pfp.KeyDown += (sender, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
            }
            else
            {
                if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LeftCtrl
                    or Key.RightCtrl or Key.LWin or Key.RWin)
                {
                    return;
                }

                Gesture = new KeyGesture(e.Key, e.KeyModifiers);
            }
        };
        pfp.MinWidth = 200;
        pfp.Confirmed += OnFlyoutConfirmed;
        pfp.Dismissed += (_, _) => Hide();

        return pfp;
    }

    protected override void OnConfirmed()
    {
        Confirmed?.Invoke(this, EventArgs.Empty);
        Hide();
    }

    private void OnFlyoutConfirmed(PickerFlyoutPresenter sender, object args)
    {
        OnConfirmed();
    }

    protected override bool ShouldShowConfirmationButtons() => true;
}
