using Avalonia.Controls;
using Beutl.Agents;
using FluentIcons.Common;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private AgentDiagnostic? _localDiagnostic;
    private AgentDiagnostic? _dismissedDiagnostic;

    public AgentDiagnosticItem? Diagnostic { get; private set => Set(ref field, value); }

    private void SetError(string? error)
    {
        _localDiagnostic = error is null ? null : AgentDiagnosticClassifier.FromFailure(error, SelectedProvider?.Descriptor);
        UpdateDiagnostic();
    }

    private void UpdateDiagnostic()
    {
        var issue = _localDiagnostic ?? _controller?.Diagnostic ?? SelectedProvider?.Diagnostic;
        if (ReferenceEquals(issue, _dismissedDiagnostic))
            issue = null;
        if (!ReferenceEquals(issue, Diagnostic?.Issue))
            Diagnostic = issue is null ? null : new AgentDiagnosticItem(issue);
    }

    public void DismissDiagnostic()
    {
        _dismissedDiagnostic = Diagnostic?.Issue;
        UpdateDiagnostic();
    }

    public Task RecheckDiagnosticAsync() => RecheckIssueAsync(Diagnostic?.Issue);

    private Task RecheckIssueAsync(AgentDiagnostic? issue) => ManageAsync(async ct =>
    {
        await RefreshProvidersAsync();
        if (issue is not { Provider: { } provider } || !ReferenceEquals(Diagnostic?.Issue, issue)
            || _detectedProviders.FirstOrDefault(choice => choice.Descriptor.Id == provider.Id) is not { } refreshed)
            return;

        bool resolved = issue.Kind switch
        {
            AgentDiagnosticKind.AuthRequired => await Task.Run(
                () => _host.Runtime.CheckProviderAuthenticationAsync(refreshed.Descriptor, ct), ct) == true,
            AgentDiagnosticKind.CliMissing or AgentDiagnosticKind.OutdatedCli => refreshed.IsAvailable
                && refreshed.Diagnostic is null && (issue.Kind != AgentDiagnosticKind.OutdatedCli || refreshed.CliVersion is not null),
            _ => false
        };
        if (resolved && ReferenceEquals(Diagnostic?.Issue, issue))
            DismissDiagnostic();
    });

    internal async Task OpenAiAgentSettingsAsync(Window owner)
    {
        try
        {
            await _host.OpenAiAgentSettingsAsync(owner);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }
    }
}

internal sealed class AgentDiagnosticItem(AgentDiagnostic issue) : AgentBindable
{
    public AgentDiagnostic Issue { get; } = issue;
    public string Title => Issue.Kind switch
    {
        AgentDiagnosticKind.CliMissing => AgentStrings.DiagnosticCliMissing,
        AgentDiagnosticKind.AuthRequired => AgentStrings.DiagnosticAuthRequired,
        AgentDiagnosticKind.OutdatedCli => AgentStrings.DiagnosticOutdatedCli,
        AgentDiagnosticKind.RateLimited => AgentStrings.DiagnosticRateLimited,
        AgentDiagnosticKind.McpUnavailable => AgentStrings.DiagnosticMcpUnavailable,
        _ => AgentStrings.DiagnosticGeneric
    };
    public string Description => Issue.Kind switch
    {
        AgentDiagnosticKind.CliMissing when HasCommand => string.Format(AgentStrings.DiagnosticInstall, ProviderName),
        AgentDiagnosticKind.CliMissing => string.Format(AgentStrings.DiagnosticExecutablePath, ProviderName),
        AgentDiagnosticKind.AuthRequired => string.Format(
            HasCommand ? AgentStrings.DiagnosticSignIn : AgentStrings.DiagnosticCredentials, ProviderName),
        AgentDiagnosticKind.OutdatedCli when Issue.CliVersion is { } version && Issue.MinimumVersion is { } minimum =>
            string.Format(AgentStrings.DiagnosticVersion, ProviderName, version, minimum),
        AgentDiagnosticKind.OutdatedCli => string.Format(AgentStrings.DiagnosticUpdate, ProviderName),
        AgentDiagnosticKind.RateLimited => AgentStrings.DiagnosticRetryLater,
        AgentDiagnosticKind.McpUnavailable => AgentStrings.DiagnosticCheckMcp,
        _ => AgentStrings.DiagnosticReviewDetails
    };
    private string ProviderName => Issue.Provider?.DisplayName ?? AgentStrings.Provider;
    public Icon StatusIcon => Issue.Kind switch
    {
        AgentDiagnosticKind.AuthRequired => Icon.PersonLock,
        AgentDiagnosticKind.McpUnavailable => Icon.PlugDisconnected,
        AgentDiagnosticKind.RateLimited => Icon.Clock,
        _ => Icon.Warning
    };
    public bool HasCommand => Issue.Command is { Length: > 0 };
    public bool HasDetails => !string.IsNullOrWhiteSpace(Issue.RawMessage);
    public bool ShowSettings => Issue.Kind is AgentDiagnosticKind.CliMissing or AgentDiagnosticKind.AuthRequired
        or AgentDiagnosticKind.OutdatedCli or AgentDiagnosticKind.McpUnavailable;
    public bool CanRecheck => Issue.Kind is AgentDiagnosticKind.CliMissing or AgentDiagnosticKind.OutdatedCli
        || Issue.Kind == AgentDiagnosticKind.AuthRequired && HasCommand;
    public bool IsDetailExpanded { get; set => Set(ref field, value); }
    public Icon CopyIcon { get; set => Set(ref field, value); } = Icon.Copy;
}
