using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Media;
using Avalonia.Media.Imaging;

using BeUtl.Framework.Service;
using BeUtl.Language;

using Firebase.Auth;
using Firebase.Auth.UI;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using S = BeUtl.Language.StringResources;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class AccountSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly HttpClient _httpClient = ServiceLocator.Current.GetRequiredService<HttpClient>();
    private readonly INotificationService _notification = ServiceLocator.Current.GetRequiredService<INotificationService>();

    public AccountSettingsPageViewModel()
    {
        IObservable<User?> user = FirebaseUI.Instance.Client.GetUserObservable();
        User = user.ToReactiveProperty()
            .AddTo(_disposables);
        DisplayName = User.Select(u => u?.Info?.DisplayName)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        SignedIn = User.Select(u => u != null)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        HasProfileImage = User.Select(u => u?.Info?.PhotoUrl != null)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        SignInWithGoogle = User.Select(u => u?.Credential?.ProviderType == FirebaseProviderType.Google)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SignOut = new(SignedIn);
        SignOut.Subscribe(async () => await FirebaseUI.Instance.Client.SignOutAsync());

        User.Subscribe(async user =>
        {
            if (user?.Info?.PhotoUrl is string photoUrl && !string.IsNullOrWhiteSpace(photoUrl))
            {
                try
                {
                    byte[] byteArray = await _httpClient.GetByteArrayAsync(photoUrl);
                    using var stream = new MemoryStream(byteArray);
                    ProfileImage.Value = new Bitmap(stream);
                }
                catch
                {
                }
            }
        }).AddTo(_disposables);

        DisplayNameInput.SetValidateNotifyError(str =>
        {
            if (!string.IsNullOrWhiteSpace(str))
            {
                return null!;
            }
            else
            {
                return StringResources.Warning.NameCannotBeLeftBlank;
            }
        });
        ChangeDisplayName = new AsyncReactiveCommand(DisplayNameInput.ObserveHasErrors.Select(b => !b));
        ChangeDisplayName.Subscribe(async () =>
        {
            if (User.Value != null)
            {
                await User.Value.ChangeDisplayNameAsync(DisplayNameInput.Value);
                User.ForceNotify();
                DisplayNameInput.Value = null!;
            }
        }).AddTo(_disposables);

        DeleteAccount = new ReactiveCommand<User>();
        DeleteAccount.Subscribe(async user =>
        {
            if (user != null)
            {
                try
                {
                    await user.DeleteAsync();
                    _notification.Show(new Notification(
                        string.Empty,
                        S.Message.YourAccountHasBeenDeleted,
                        NotificationType.Success));
                }
                catch
                {
                    _notification.Show(new Notification(
                        string.Empty,
                        S.Message.OperationCouldNotBeExecuted,
                        NotificationType.Warning));
                }
            }
        }).AddTo(_disposables);

        ChangePassword = new ReactiveCommand();
        ChangePassword.Subscribe(async () =>
        {
            User? user = User.Value;
            if (user != null)
            {
                try
                {
                    ChangePasswordInProgress.Value = true;
                    UserCredential userCredential = await FirebaseUI.Instance.Client.SignInWithEmailAndPasswordAsync(user.Info.Email, Password.Value);
                    await userCredential.User.ChangePasswordAsync(NewPassword.Value);
                    Password.Value = "";
                    NewPassword.Value = "";
                    ChangePasswordError.Value = "";
                    _notification.Show(new Notification(
                        string.Empty,
                        S.Message.PasswordHasBeenChanged,
                        NotificationType.Success));
                }
                catch (FirebaseAuthException ex)
                {
                    ChangePasswordError.Value = FirebaseErrorLookup.LookupError(ex);
                }
                finally
                {
                    ChangePasswordInProgress.Value = false;
                }
            }
        }).AddTo(_disposables);
    }

    public ReactiveProperty<User?> User { get; }

    public ReactivePropertySlim<IImage?> ProfileImage { get; } = new();

    public ReadOnlyReactivePropertySlim<string?> DisplayName { get; }

    public ReadOnlyReactivePropertySlim<bool> SignedIn { get; }

    public ReadOnlyReactivePropertySlim<bool> HasProfileImage { get; }

    public ReadOnlyReactivePropertySlim<bool> SignInWithGoogle { get; }

    public ReactiveCommand SignOut { get; }

    public ReactiveProperty<string> DisplayNameInput { get; } = new();

    public AsyncReactiveCommand ChangeDisplayName { get; }

    public ReactiveCommand<User> DeleteAccount { get; }

    public ReactivePropertySlim<string> Password { get; } = new();

    public ReactivePropertySlim<string> NewPassword { get; } = new();

    public ReactivePropertySlim<string> ChangePasswordError { get; } = new();

    public ReactivePropertySlim<bool> ChangePasswordInProgress { get; } = new();

    public ReactiveCommand ChangePassword { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
