using System.Diagnostics;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;

namespace Beutl.Services;

public interface IAiPlanCoordinator
{
    void OpenAccountSettings();

    void OpenAiPlan();

    Task RefreshIfPendingAsync(CancellationToken cancellationToken);
}

internal sealed class AiPlanCoordinator : IAiPlanCoordinator
{
    private readonly BeutlApiApplication _application;
    private readonly IAiEntitlementService _entitlements;
    private readonly Action<Uri> _openUri;
    private readonly Func<string> _language;
    private int _refreshPending;

    public AiPlanCoordinator(
        BeutlApiApplication application,
        IAiEntitlementService entitlements,
        Action<Uri>? openUri = null,
        Func<string>? language = null)
    {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _openUri = openUri ?? OpenWithShell;
        _language = language ?? (() =>
            GlobalConfiguration.Instance.ViewConfig.UICulture.TwoLetterISOLanguageName);
    }

    public void OpenAccountSettings()
    {
        _openUri(_application.AccountSettingsUri);
        Interlocked.Exchange(ref _refreshPending, 1);
    }

    public void OpenAiPlan()
    {
        string language = _language();
        if (string.IsNullOrWhiteSpace(language))
            throw new InvalidOperationException("The UI language is unavailable.");

        _openUri(_application.GetAiPlanUri(language));
        Interlocked.Exchange(ref _refreshPending, 1);
    }

    public async Task RefreshIfPendingAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshPending, 0) == 0)
            return;

        try
        {
            await _entitlements.RefreshAsync(cancellationToken);
        }
        catch
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            throw;
        }
    }

    private static void OpenWithShell(Uri uri)
    {
        Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
        {
            UseShellExecute = true,
            Verb = "open",
        });
    }
}
