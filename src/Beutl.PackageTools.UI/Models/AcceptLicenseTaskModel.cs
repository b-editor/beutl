using Beutl.Logging;

using NuGet.Packaging;
using NuGet.Packaging.Core;

using Reactive.Bindings;
using Reactive.Bindings.TinyLinq;

namespace Beutl.PackageTools.UI.Models;

public record LicenseItem(PackageIdentity Package, LicenseMetadata License)
{
    public string Body
    {
        get
        {
            return License.License;
        }
    }

    public string ShortName
    {
        get
        {
            return License.LicenseExpression?.ToString() ?? License.License;
        }
    }
}

public class AcceptLicenseTaskModel
{
    private readonly ILogger _logger = Log.CreateLogger<AcceptLicenseTaskModel>();
    private readonly BeutlApiApplication _app;
    private readonly PackageInstallContext _context;
    private readonly AcceptedLicenseManager _acceptedLicenseManager;
    private TaskCompletionSource<bool>? _userInput;

    public AcceptLicenseTaskModel(BeutlApiApplication app, PackageInstallContext context)
    {
        _app = app;
        _context = context;
        _acceptedLicenseManager = _app.GetResource<AcceptedLicenseManager>();
        IsAcceptedNull = IsAccepted.Select(v => v.HasValue)
            .ToReadOnlyReactivePropertySlim();

        var licensesRequiringApproval = _context.LicensesRequiringApproval
            .Where(x => !_acceptedLicenseManager.Accepted.ContainsKey(x.Item1))
            .Select(x => new LicenseItem(x.Item1, x.Item2))
            .ToArray();
        if (licensesRequiringApproval.Length > 0)
        {
            Licenses.Value = licensesRequiringApproval;
            RequireAccept.Value = true;
        }
    }

    public ReactiveProperty<bool> ShowDetails { get; } = new(false);

    public ReactiveProperty<bool> RequireAccept { get; } = new();

    public ReactiveProperty<LicenseItem[]?> Licenses { get; } = new();

    public ReactiveProperty<bool> IsRunning { get; } = new();

    public ReactiveProperty<bool?> IsAccepted { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> IsAcceptedNull { get; }

    public ReactiveProperty<bool> Succeeded { get; } = new();

    public ReactiveProperty<bool> Failed { get; } = new();

    public void AcceptAll(bool value)
    {
        IsAccepted.Value = value;
        _userInput?.SetResult(value);
        _logger.LogInformation("User accepted all licenses: {Accepted}", value);
    }

    public async Task<bool> Run(CancellationToken token)
    {
        _logger.LogInformation("License acceptance task started.");
        IsRunning.Value = true;

        try
        {
            // 同意が必要なライセンスの内、未同意のものがある場合、同意させる
            if (RequireAccept.Value)
            {
                Task<bool>? task;
                if (!IsAccepted.Value.HasValue)
                {
                    _userInput = new TaskCompletionSource<bool>();
                    task = _userInput.Task;
                    _logger.LogInformation("Waiting for user input on license acceptance.");
                }
                else
                {
                    task = Task.FromResult(IsAccepted.Value.Value);
                }

                if (!await task.WaitAsync(token))
                {
                    _logger.LogWarning("User did not accept the licenses.");
                    Failed.Value = false;
                    return false;
                }
                else
                {
                    // 同意したことを記録
                    _acceptedLicenseManager.Accepts(Licenses.Value!.Select(v => (v.Package, v.License)).ToArray());
                    _logger.LogInformation("User accepted the licenses.");
                }
            }

            Succeeded.Value = true;
            _logger.LogInformation("License acceptance task succeeded.");
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("License acceptance task was canceled.");
            Failed.Value = true;
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An exception occurred during the license acceptance task.");
            Failed.Value = true;
            return false;
        }
        finally
        {
            IsRunning.Value = false;
            _logger.LogInformation("License acceptance task ended.");
        }
    }
}
