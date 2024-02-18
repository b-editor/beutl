using Beutl.Logging;

using Reactive.Bindings;

namespace Beutl.PackageTools.UI.Models;

public class VerifyTaskModel : IProgress<double>
{
    private readonly ILogger _logger = Log.CreateLogger<VerifyTaskModel>();
    private readonly BeutlApiApplication _app;
    private readonly PackageInstallContext _context;
    private TaskCompletionSource<bool>? _userInput;

    public VerifyTaskModel(BeutlApiApplication app, PackageInstallContext context)
    {
        _app = app;
        _context = context;
    }

    public ReactiveProperty<bool> ShowDetails { get; } = new(false);

    // {PackageFileName}をダウンロード中
    public ReactiveProperty<string> VerifyMessage { get; } = new();

    public ReactiveProperty<double> Progress { get; } = new();

    public ReactiveProperty<bool> IsIndeterminate { get; } = new(false);

    public ReactiveProperty<bool> FailedToVerify { get; } = new();

    public ReactiveProperty<bool> IsProgressBarVisible { get; } = new();

    public ReactiveProperty<string> ErrorMessage { get; } = new();

    public ReactiveProperty<bool> IsRunning { get; } = new();

    public ReactiveProperty<bool?> IsContinued { get; } = new();

    public ReactiveProperty<bool> Skipped { get; } = new();

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public void Continue(bool value)
    {
        IsContinued.Value = value;
        _userInput?.SetResult(value);
    }

    public async Task<bool> Run(CancellationToken token)
    {
        PackageInstaller installer = _app.GetResource<PackageInstaller>();
        IsRunning.Value = true;

        try
        {
            if (_context.Asset is { } asset
                && _context.NuGetPackageFile != null)
            {
                IsProgressBarVisible.Value = true;
                IsIndeterminate.Value = true;

                VerifyMessage.Value = Strings.VerifyingHashCode;
                await installer.VerifyPackageFile(_context, this, token);
                VerifyMessage.Value = Strings.Verified;

                FailedToVerify.Value = !_context.HashVerified;
                if (!_context.HashVerified)
                {
                    _logger.LogWarning(
                        "Verify failed. ({PackageId}/{Version})",
                        _context.PackageName, _context.Version);

                    Task<bool>? task;
                    if (!IsContinued.Value.HasValue)
                    {
                        _userInput = new TaskCompletionSource<bool>();
                        task = _userInput.Task;
                    }
                    else
                    {
                        task = Task.FromResult(IsContinued.Value.Value);
                    }

                    if (!await task.WaitAsync(token))
                    {
                        Failed.Value = false;
                        return false;
                    }
                }
            }
            else
            {
                Skipped.Value = true;
            }

            Succeeded.Value = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            ErrorMessage.Value = Strings.Operation_canceled;
            Failed.Value = true;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred while verifying the hash code.");
            ErrorMessage.Value = ex.Message;
            Failed.Value = true;
            return false;
        }
        finally
        {
            IsIndeterminate.Value = false;
            IsRunning.Value = false;
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
