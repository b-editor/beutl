using Avalonia.Data.Converters;
using Beutl.Api.Clients;
using Beutl.Logging;
using Reactive.Bindings;
using Reactive.Bindings.TinyLinq;
using Refit;

namespace Beutl.PackageTools.UI.Models;

public class DownloadTaskModel : IProgress<double>
{
    private readonly ILogger _logger = Log.CreateLogger<DownloadTaskModel>();
    private readonly BeutlApiApplication _app;
    private readonly PackageChangeModel _model;
    private TaskCompletionSource<bool>? _userInput;

    public DownloadTaskModel(PackageChangeModel model, BeutlApiApplication app)
    {
        _app = app;
        _model = model;
        PackageFileName = $"{_model.Id}.{_model.Version}.nupkg";
        IsLocalSourcePreferredNull = IsLocalSourcePreferred.Select(v => v.HasValue)
            .ToReadOnlyReactivePropertySlim();
    }

    public string PackageFileName { get; }

    // {PackageFileName}をダウンロード中
    public ReactiveProperty<string> DownloadMessage { get; } = new();

    public ReactiveProperty<double> Progress { get; } = new();

    public ReactiveProperty<bool> IsIndeterminate { get; } = new(false);

    public ReactiveProperty<bool> ShowDetails { get; } = new(false);

    public ReactiveProperty<bool> IsProgressBarVisible { get; } = new();

    public ReactiveProperty<bool> DownloadSkipped { get; } = new();

    public bool Conflict => _model.Conflict;

    public ReactiveProperty<bool?> IsLocalSourcePreferred { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsLocalSourcePreferredNull { get; }

    public PackageInstallContext? Context { get; private set; }

    public ReactiveProperty<string> ErrorMessage { get; } = new();

    public ReactiveProperty<bool> IsRunning { get; } = new();

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public void SetPreferLocalSource(bool value)
    {
        IsLocalSourcePreferred.Value = value;
        _userInput?.SetResult(value);
    }

    public async Task<bool> Run(CancellationToken token)
    {
        PackageInstaller installer = _app.GetResource<PackageInstaller>();
        DiscoverService discover = _app.GetResource<DiscoverService>();
        IsRunning.Value = true;
        _logger.LogInformation("Download task started for package {PackageId} version {Version}.", _model.Id, _model.Version);

        try
        {
            if (Conflict)
            {
                Task<bool>? task;
                if (!IsLocalSourcePreferred.Value.HasValue)
                {
                    _userInput = new TaskCompletionSource<bool>();
                    task = _userInput.Task;
                    _logger.LogInformation("Waiting for user input to resolve conflict for package {PackageId}.", _model.Id);
                }
                else
                {
                    task = Task.FromResult(IsLocalSourcePreferred.Value.Value);
                }

                if (await task.WaitAsync(token))
                {
                    DownloadSkipped.Value = true;
                    Context = installer.PrepareForInstall(_model.Id, _model.Version.ToString(), true, token);
                    Succeeded.Value = true;
                    _logger.LogInformation("Download skipped for package {PackageId} version {Version}.", _model.Id, _model.Version);
                    return true;
                }
            }

            IsProgressBarVisible.Value = true;
            IsIndeterminate.Value = true;

            Package package = await discover.GetPackage(_model.Id);
            Release release = await package.GetReleaseAsync(_model.Version.ToString());

            Context = await installer.PrepareForInstall(release, true, token);
            DownloadMessage.Value = string.Format(Strings.Downloading_XXX, PackageFileName);
            _logger.LogInformation("Downloading package file {PackageFileName}.", PackageFileName);
            await installer.DownloadPackageFile(Context, this, token);
            DownloadMessage.Value = string.Format(Strings.Downloaded_XXX, PackageFileName);
            _logger.LogInformation("Downloaded package file {PackageFileName}.", PackageFileName);

            Succeeded.Value = true;
            return true;
        }
        catch (ApiException apiEx)
        {
            var errorResponse = await apiEx.GetContentAsAsync<ApiErrorResponse>();
            _logger.LogError(apiEx, "API exception occurred while downloading package {PackageId} version {Version}.", _model.Id, _model.Version);
            ErrorMessage.Value = errorResponse?.Message ?? apiEx.Message;
            Failed.Value = true;
            return false;
        }
        catch (OperationCanceledException)
        {
            if (File.Exists(Context?.NuGetPackageFile))
            {
                File.Delete(Context.NuGetPackageFile);
            }

            ErrorMessage.Value = Strings.Operation_canceled;
            _logger.LogWarning("Download operation canceled for package {PackageId} version {Version}.", _model.Id, _model.Version);
            Failed.Value = true;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected exception occurred while downloading package {PackageId} version {Version}.", _model.Id, _model.Version);
            ErrorMessage.Value = ex.Message;
            Failed.Value = true;
            return false;
        }
        finally
        {
            IsIndeterminate.Value = false;
            IsRunning.Value = false;
            _logger.LogInformation("Download task ended for package {PackageId} version {Version}.", _model.Id, _model.Version);
        }
    }

    void IProgress<double>.Report(double value)
    {
        if (double.IsFinite(value))
        {
            Progress.Value = value * 100;
            IsIndeterminate.Value = false;
        }
        else
        {
            IsIndeterminate.Value = true;
        }
    }
}
