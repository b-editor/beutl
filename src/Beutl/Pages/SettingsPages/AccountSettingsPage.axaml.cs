using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.ViewModels.SettingsPages;

using Reactive.Bindings.Extensions;

namespace Beutl.Pages.SettingsPages;

public sealed partial class AccountSettingsPage : UserControl
{
    private TopLevel? _topLevel;

    public AccountSettingsPage()
    {
        InitializeComponent();
        IObservable<AccountSettingsPageViewModel?> viewModel = this.GetObservable(DataContextProperty)
            .Select(v => v as AccountSettingsPageViewModel);

        IObservable<bool?> signedIn = viewModel
            .Select(v => v?.SignedIn.Select(v => (bool?)v) ?? Observable.ReturnThenNever<bool?>(null))
            .Switch();

        signedIn
            .Where(v => v == false)
            .Take(1)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => signInContainer.Content = new SignInScreen());

        signedIn
            .Where(v => v == true)
            .Take(1)
            .ObserveOnUIDispatcher()
            .Subscribe(_ => settingsContainer.Content = new AccountSettingsScreen());
    }

    // The AI plan is managed in a browser. Reload the plan state once the app
    // window is focused again so a change made there is reflected here.
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is WindowBase window)
        {
            window.Activated += OnWindowActivated;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (_topLevel is WindowBase window)
        {
            window.Activated -= OnWindowActivated;
        }

        _topLevel = null;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        (DataContext as AccountSettingsPageViewModel)?.NotifyReturnedToApplication();
    }
}
