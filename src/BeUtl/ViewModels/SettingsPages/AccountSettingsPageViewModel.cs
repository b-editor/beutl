
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Skia;

using BeUtl.Framework.Service;
using BeUtl.Services;

using Firebase.Auth;
using Firebase.Auth.Providers;
using Firebase.Auth.UI;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

using SkiaSharp;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class AccountSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly HttpClient _httpClient = ServiceLocator.Current.GetRequiredService<HttpClient>();
    private readonly INotificationService _notification = ServiceLocator.Current.GetRequiredService<INotificationService>();
    private readonly AccountService _accountService = ServiceLocator.Current.GetRequiredService<AccountService>();

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

        IObservable<FetchUserProvidersResult?> fetchProviders = User.SelectMany(u => u == null
            ? Task.FromResult((FetchUserProvidersResult?)null)
            : FirebaseUI.Instance.Client.FetchSignInMethodsForEmailAsync(u.Info.Email));
        SignInWithGoogle = fetchProviders
            .Select(r => r?.SignInProviders.Contains(FirebaseProviderType.Google) ?? false)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        SignInWithEmail = fetchProviders
            .Select(r => r?.SignInProviders.Contains(FirebaseProviderType.EmailAndPassword) ?? false)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        SignInWithGoogleAndEmail = SignInWithEmail
            .CombineLatest(SignInWithGoogle)
            .Select(t => t.First && t.Second)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);
        SignInWithEmailOnly = SignInWithEmail
            .CombineLatest(SignInWithGoogle)
            .Select(t => t.First && !t.Second)
            .ToReadOnlyReactivePropertySlim()
            .AddTo(_disposables);

        SignOut = new(SignedIn);
        SignOut.Subscribe(async () => await FirebaseUI.Instance.Client.SignOutAsync());

        UploadPhotoImage = new(SignedIn);
        UploadPhotoImage.Subscribe(async file =>
        {
            if (User.Value != null&& file.CanOpenRead)
            {
                const int SIZE = 400;
                var dstBmp = new SKBitmap(SIZE, SIZE, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using (Stream stream = await file.OpenReadAsync())
                using (var srcBmp = SKBitmap.Decode(stream))
                using (var canvas = new SKCanvas(dstBmp))
                {
                    float x = SIZE / (float)srcBmp.Width;
                    float y = SIZE / (float)srcBmp.Height;
                    float w = srcBmp.Width * MathF.Max(x, y);
                    float h = srcBmp.Height * MathF.Max(x, y);
                    Rect rect = new Rect(0, 0, SIZE, SIZE)
                        .CenterRect(new Rect(0, 0, w, h));
                    canvas.DrawBitmap(srcBmp, rect.ToSKRect());
                    canvas.Flush();
                }

                using var dstStream = new MemoryStream();
                dstBmp.Encode(dstStream, SKEncodedImageFormat.Jpeg, 100);
                dstBmp.Dispose();
                dstStream.Position = 0;

                await _accountService.UploadProfileImage(User.Value, dstStream);
                User.ForceNotify();
            }
            file.Dispose();
        });

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
                return S.Warning.NameCannotBeLeftBlank;
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
                    await _accountService.DeleteAccount(user);
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

        UnlinkFromGoogle = new ReactiveCommand();
        UnlinkFromGoogle.Subscribe(async () =>
        {
            User? user = User.Value;
            if (user != null)
            {
                try
                {
                    await user.UnlinkAsync(FirebaseProviderType.Google);

                    _notification.Show(new Notification(
                        string.Empty,
                        S.Message.PasswordHasBeenChanged,
                        NotificationType.Success));

                    User.ForceNotify();
                }
                catch (FirebaseAuthHttpException ex)
                {
                    _notification.Show(new Notification(
                        string.Empty,
                        FirebaseErrorLookup.LookupError(ex),
                        NotificationType.Error));
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

    public ReadOnlyReactivePropertySlim<bool> SignInWithEmail { get; }

    public ReadOnlyReactivePropertySlim<bool> SignInWithGoogleAndEmail { get; }

    public ReadOnlyReactivePropertySlim<bool> SignInWithEmailOnly { get; }

    public ReactiveCommand SignOut { get; }

    public AsyncReactiveCommand<IStorageFile> UploadPhotoImage { get; }

    public ReactiveProperty<string> DisplayNameInput { get; } = new();

    public AsyncReactiveCommand ChangeDisplayName { get; }

    public ReactiveCommand<User> DeleteAccount { get; }

    public ReactivePropertySlim<string> Password { get; } = new();

    public ReactivePropertySlim<string> NewPassword { get; } = new();

    public ReactivePropertySlim<string> ChangePasswordError { get; } = new();

    public ReactivePropertySlim<bool> ChangePasswordInProgress { get; } = new();

    public ReactiveCommand ChangePassword { get; }

    public ReactiveCommand UnlinkFromGoogle { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
