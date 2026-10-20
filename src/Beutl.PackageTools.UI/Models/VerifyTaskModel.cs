using Beutl.Logging;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

public class VerifyTaskModel : ITaskStepModel
{
    private readonly ILogger _logger = Log.CreateLogger<VerifyTaskModel>();
    private readonly BeutlApiApplication _app;
    private readonly PackageInstallContext _context;

    public VerifyTaskModel(BeutlApiApplication app, PackageInstallContext context)
    {
        _app = app;
        _context = context;
    }

    public ReactiveProperty<bool> ShowDetails { get; } = new(false);

    public ReactiveProperty<string> VerifyMessage { get; } = new();

    public ProgressState Progress { get; } = new();

    public ReactiveProperty<bool> FailedToVerify { get; } = new();

    public ReactiveProperty<bool> IsProgressBarVisible { get; } = new();

    public ReactiveProperty<string> ErrorMessage { get; } = new();

    public ReactiveProperty<bool> IsRunning { get; } = new();

    public UserAnswer ContinueAnswer { get; } = new();

    public ReactiveProperty<bool> Skipped { get; } = new();

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public void Continue(bool value)
    {
        ContinueAnswer.Answer(value);
    }

    public async Task<bool> Run(CancellationToken token)
    {
        PackageInstaller installer = _app.GetResource<PackageInstaller>();
        IsRunning.Value = true;
        _logger.LogInformation("Verification process started.");

        try
        {
            if (_context.Asset is not null
                && _context.NuGetPackageFile != null)
            {
                IsProgressBarVisible.Value = true;
                Progress.IsIndeterminate.Value = true;

                VerifyMessage.Value = Strings.VerifyingHashCode;
                _logger.LogInformation("Verifying hash code for package: {PackageName}, version: {Version}", _context.PackageName, _context.Version);
                await installer.VerifyPackageFile(_context, Progress, token);
                VerifyMessage.Value = Strings.Verified;

                FailedToVerify.Value = !_context.HashVerified;
                if (!_context.HashVerified)
                {
                    _logger.LogWarning("Hash verification failed for package: {PackageName}, version: {Version}", _context.PackageName, _context.Version);

                    if (!await ContinueAnswer.WaitAsync(token))
                    {
                        _logger.LogInformation("User chose not to continue after verification failure.");
                        return false;
                    }
                }
            }
            else
            {
                _logger.LogInformation("Verification skipped due to missing asset or package file.");
                Skipped.Value = true;
            }

            _logger.LogInformation("Verification succeeded for package: {PackageName}, version: {Version}", _context.PackageName, _context.Version);
            Succeeded.Value = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Verification operation was canceled.");
            ErrorMessage.Value = Strings.Operation_canceled;
            Failed.Value = true;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while verifying the hash code for package: {PackageName}, version: {Version}", _context.PackageName, _context.Version);
            ErrorMessage.Value = ex.Message;
            Failed.Value = true;
            return false;
        }
        finally
        {
            Progress.IsIndeterminate.Value = false;
            IsRunning.Value = false;
            _logger.LogInformation("Verification process ended.");
        }
    }
}
