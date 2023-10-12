using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

using Beutl.ViewModels.SettingsPages;

using Reactive.Bindings.Extensions;

namespace Beutl.Pages.SettingsPages;

public sealed partial class AccountSettingsPage : UserControl
{
    public AccountSettingsPage()
    {
        InitializeComponent();
        IObservable<AccountSettingsPageViewModel?> viewModel = this.GetObservable(DataContextProperty)
            .Select(v => v as AccountSettingsPageViewModel);

        IObservable<bool?> signedIn = viewModel
            .Select(v => v?.SignedIn.Select(v => (bool?)v) ?? Observable.Return<bool?>(null))
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
}
