using Avalonia.Platform.Storage;

using Beutl.Api;
using Beutl.Api.Objects;
using Beutl.Logging;
using Beutl.Services;

using Microsoft.Extensions.Logging;

using OpenTelemetry.Trace;

using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public class CreateAssetViewModel
{
    private readonly ILogger<CreateAssetViewModel> _logger = Log.CreateLogger<CreateAssetViewModel>();
    private readonly AuthorizedUser _user;
    private CancellationTokenSource? _cts;

    public CreateAssetViewModel(AuthorizedUser user, FilePickerFileType? requestContentType = null)
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
            .Select(x => x is >= 0 and <= 2 ? Strings.Next : null)
            .ToReadOnlyReactivePropertySlim()!;

        CloseButtonText = PageIndex.CombineLatest(Submitting)
            .Select(x => x.First is >= 0 and <= 2 || x.Second ? Strings.Cancel : Strings.Close)
            .ToReadOnlyReactivePropertySlim(Strings.Close)!;

        UseInternalServer = SelectedMethod.Select(x => x == 0).ToReadOnlyReactivePropertySlim();
        UseExternalServer = SelectedMethod.Select(x => x == 1).ToReadOnlyReactivePropertySlim();

        File.SetValidateNotifyError(file => !System.IO.File.Exists(file) ? Message.FileDoesNotExist : null);
        Url.SetValidateNotifyError(url => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? null!
                : Message.InvalidUrl);
        _user = user;
        RequestContentType = requestContentType;

        if (RequestContentType != null)
        {
            ContentType.Value = RequestContentType.MimeTypes?[0] ?? "";
        }

        Name.SetValidateNotifyError(async x =>
        {
            if (string.IsNullOrWhiteSpace(x))
            {
                return Message.NameCannotBeLeftBlank;
            }

            try
            {
                using (await _user.Lock.LockAsync())
                {
                    _ = await _user.Profile.GetAssetAsync(x);
                    return Message.ItAlreadyExists;
                }
            }
            catch
            {
                return null;
            }
        });
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

    public FilePickerFileType? RequestContentType { get; }

    public async Task SubmitAsync()
    {
        using Activity? activity = Telemetry.StartActivity("CreateAsset.Submit");
        try
        {
            _cts = new CancellationTokenSource();
            CancellationToken cancellationToken = _cts.Token;

            Submitting.Value = true;
            using (await _user.Lock.LockAsync(cancellationToken))
            {
                await _user.RefreshAsync();
                if (SelectedMethod.Value == 0)
                {
                    ProgressStatus.Value = Strings.CreateAsset_UploadingFiles;
                    using FileStream stream = System.IO.File.OpenRead(File.Value);
                    _ = RunProgressReporter(stream, cancellationToken);
                    Result = await _user.Profile.AddAssetAsync(Name.Value, stream, ContentType.Value);
                }
                else
                {
                    ProgressStatus.Value = Strings.CreateAsset_PerformingAnOperation;
                    Result = await _user.Profile.AddAssetAsync(Name.Value, new CreateVirtualAssetRequest()
                    {
                        ContentType = ContentType.Value,
                        Url = Url.Value,
                        Sha256 = Sha256.Value,
                        Sha384 = Sha384.Value,
                        Sha512 = Sha512.Value,
                    });
                }
                await Result.UpdateAsync(true);

                ProgressStatus.Value = Strings.CreateAsset_Completed;
            }
        }
        catch (BeutlApiException<ApiErrorResponse> ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(ex, "Failed to upload a file.");
            ProgressStatus.Value = Message.AnUnexpectedErrorHasOccurred;
            Error.Value = ex.Result.Message;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            _logger.LogError(e, "Failed to upload a file.");
            ProgressStatus.Value = Message.AnUnexpectedErrorHasOccurred;
            Error.Value = e.Message;
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
