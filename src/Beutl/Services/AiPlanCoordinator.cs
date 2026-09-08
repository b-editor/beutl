using System.Diagnostics;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Services;

public interface IAiPlanCoordinator
{
    /// <summary>Raised once after a pending entitlement refresh completes successfully.</summary>
    event EventHandler? Refreshed;

    void OpenAccountSettings();

    void OpenAiPlan();

    /// <summary>
    /// Refreshes entitlements after a plan page was opened by this coordinator.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when this call consumed and completed a pending refresh;
    /// <see langword="false"/> when no plan refresh was pending.
    /// </returns>
    /// <remarks>A failed or cancelled refresh is restored as pending before the exception propagates.</remarks>
    Task<bool> RefreshIfPendingAsync(CancellationToken cancellationToken);
}

internal sealed class AiPlanCoordinator : IAiPlanCoordinator
{
    private readonly ILogger _logger = Log.CreateLogger<AiPlanCoordinator>();
    private readonly IAiEntitlementService _entitlements;
    private readonly Action<Uri> _openUri;
    private readonly Func<string> _language;
    private readonly Uri _portalBaseUri;
    private int _refreshPending;

    public AiPlanCoordinator(
        IAiEntitlementService entitlements,
        Action<Uri>? openUri = null,
        Func<string>? language = null,
        Uri? portalBaseUri = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _openUri = openUri ?? OpenWithShell;
        _language = language ?? (() =>
            GlobalConfiguration.Instance.ViewConfig.UICulture.TwoLetterISOLanguageName);
        // The pages opened here belong to the same site the client talks to, so
        // a build pointed at a local server must not send the user to the live
        // one to manage a plan that build knows nothing about.
        _portalBaseUri = portalBaseUri ?? new Uri(BeutlApiApplication.BaseUrl, UriKind.Absolute);
    }

    public event EventHandler? Refreshed;

    public void OpenAccountSettings()
    {
        _openUri(new Uri(_portalBaseUri, "account/manage"));
        Interlocked.Exchange(ref _refreshPending, 1);
    }

    public void OpenAiPlan()
    {
        string language = _language();
        if (string.IsNullOrWhiteSpace(language))
            throw new InvalidOperationException("The UI language is unavailable.");

        _openUri(new Uri(
            _portalBaseUri,
            $"{Uri.EscapeDataString(language)}/account/manage/ai-plan"));
        Interlocked.Exchange(ref _refreshPending, 1);
    }

    public async Task<bool> RefreshIfPendingAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _refreshPending, 0) == 0)
            return false;

        try
        {
            await _entitlements.RefreshAsync(cancellationToken);
        }
        catch
        {
            Interlocked.Exchange(ref _refreshPending, 1);
            throw;
        }

        NotifyRefreshedSafely();
        return true;
    }

    private void NotifyRefreshedSafely()
    {
        Delegate[] handlers = Refreshed?.GetInvocationList() ?? [];
        foreach (EventHandler handler in handlers.Cast<EventHandler>())
        {
            try
            {
                handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An AI plan refresh subscriber failed; continuing publication.");
            }
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
