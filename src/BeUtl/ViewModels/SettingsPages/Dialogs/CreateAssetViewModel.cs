using Beutl.Api;
using Beutl.Api.Objects;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages.Dialogs;

public class CreateAssetViewModel
{
    private readonly AuthorizedUser _user;
    private CancellationTokenSource? _cts;

    public CreateAssetViewModel(AuthorizedUser user)
    {
        IsPrimaryButtonEnabled = PageIndex.CombineLatest(
            SelectedMethod, Name.ObserveHasErrors, ContentType.ObserveHasErrors, File.ObserveHasErrors, Url.ObserveHasErrors,
            Sha256.CombineLatest(Sha384, Sha512).Select(x
                => !string.IsNullOrWhiteSpace(x.First)
                || !string.IsNullOrWhiteSpace(x.Second)
                || !string.IsNullOrWhiteSpace(x.Third)),
            Submitting)
            .Select(x =>
            {
                if (x.First == 0 && x.Second != -1)
                {
                    return true;
                }
                else if (x.First == 1 && !x.Third && !x.Fourth)
                {
                    // 名前とファイルまたはURLを入力
                    // Nameが有効な値
                    if (x.Second == 0 && !x.Fifth)
                    {
                        // 内部サーバーを使用
                        // Fileが有効な値
                        return true;
                    }
                    else if (x.Second == 1 && !x.Sixth)
                    {
                        // 外部サーバーを使用
                        // Urlが有効な値
                        return true;
                    }
                }
                else if (x.First == 2 && x.Seventh)
                {
                    // ハッシュ値入力
                    // 最低でも一つはハッシュ値を持っている
                    return true;
                }

                return false;
            })
            .ToReadOnlyReactivePropertySlim();

        PrimaryButtonText = PageIndex
            .Select(x => x is >= 0 and <= 2 ? S.Common.Next : null)
            .ToReadOnlyReactivePropertySlim()!;

        CloseButtonText = PageIndex.CombineLatest(Submitting)
            .Select(x => x.First is >= 0 and <= 2 || x.Second ? S.Common.Cancel : S.Common.Close)
            .ToReadOnlyReactivePropertySlim()!;

        UseInternalServer = SelectedMethod.Select(x => x == 0).ToReadOnlyReactivePropertySlim();
        UseExternalServer = SelectedMethod.Select(x => x == 1).ToReadOnlyReactivePropertySlim();

        File.SetValidateNotifyError(file => !System.IO.File.Exists(file) ? S.Warning.FileDoesNotExist : null);
        Url.SetValidateNotifyError(url => (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
                ? null!
                : S.Warning.InvalidURL);
        _user = user;
    }

    public ReadOnlyReactivePropertySlim<string?> PrimaryButtonText { get; }

    public ReadOnlyReactivePropertySlim<string> CloseButtonText { get; }

    public ReadOnlyReactivePropertySlim<bool> IsPrimaryButtonEnabled { get; }

    public ReadOnlyReactivePropertySlim<bool> UseInternalServer { get; }

    public ReadOnlyReactivePropertySlim<bool> UseExternalServer { get; }

    public ReactivePropertySlim<int> PageIndex { get; } = new();

    public ReactivePropertySlim<int> SelectedMethod { get; } = new(-1);

    public ReactiveProperty<string> Name { get; } = new();

    public ReactiveProperty<string> ContentType { get; } = new();

    public ReactiveProperty<string> File { get; } = new();

    public ReactiveProperty<string> Url { get; } = new("");

    public ReactivePropertySlim<string> Sha256 { get; } = new();

    public ReactivePropertySlim<string> Sha384 { get; } = new();

    public ReactivePropertySlim<string> Sha512 { get; } = new();

    public ReactivePropertySlim<string> ProgressStatus { get; } = new();

    public ReactivePropertySlim<double> ProgressValue { get; } = new();

    public ReactivePropertySlim<string> Error { get; } = new();

    public ReactivePropertySlim<bool> Submitting { get; } = new();

    public Asset? Result { get; private set; }

    public async Task SubmitAsync()
    {
        try
        {
            _cts = new CancellationTokenSource();
            CancellationToken cancellationToken = _cts.Token;

            Submitting.Value = true;
            await _user.RefreshAsync();
            if (SelectedMethod.Value == 0)
            {
                ProgressStatus.Value = S.SettingsPage.CreateAsset.UploadingFiles;
                using FileStream stream = System.IO.File.OpenRead(File.Value);
                _ = RunProgressReporter(stream, cancellationToken);
                Result = await _user.Profile.AddAssetAsync(Name.Value, stream, ContentType.Value);
            }
            else
            {
                ProgressStatus.Value = S.SettingsPage.CreateAsset.PerformingAnOperation;
                Result = await _user.Profile.AddAssetAsync(Name.Value, new CreateVirtualAssetRequest()
                {
                    ContentType = ContentType.Value,
                    Url = Url.Value,
                    Sha256 = Sha256.Value,
                    Sha384 = Sha384.Value,
                    Sha512 = Sha512.Value,
                });
            }

            ProgressStatus.Value = S.SettingsPage.CreateAsset.Completed;
        }
        catch (BeutlApiException<ApiErrorResponse> ex)
        {
            ProgressStatus.Value = S.Warning.AnUnexpectedErrorHasOccurred;
            Error.Value = ex.Result.Message;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ProgressStatus.Value = S.Warning.AnUnexpectedErrorHasOccurred;
            // Todo: 例外
            Error.Value = "Error";
        }
        finally
        {
            Submitting.Value = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    private async Task RunProgressReporter(Stream stream, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)
           && stream.CanRead)
        {
            ProgressValue.Value = stream.Position / (double)stream.Length * 100;
        }
    }
}
