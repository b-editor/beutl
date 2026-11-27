using System.ComponentModel;
using Beutl.Agents;
using Beutl.Configuration;

namespace Beutl.Editor.Components.AgentTab.ViewModels;

internal sealed partial class AgentTabViewModel
{
    private readonly SerialDisposable _providerProbe = new();
    private string? _draftProviderId;
    private bool _useDefaultPermission = true;

    private void OnRuntimeSettingsChanged(object? sender, PropertyChangedEventArgs args) => Post(() =>
    {
        if (_initialization is null)
            return;

        if (args.PropertyName is nameof(AiAgentConfig.SteerOnSend) or nameof(AiAgentConfig.ComposerSendKey))
        {
            NotifySendActions();
        }
        else if (args.PropertyName is nameof(AiAgentConfig.DefaultRuntimeProviderId)
            or nameof(AiAgentConfig.DefaultPermissionMode) or nameof(AiAgentConfig.DefaultModel)
            or nameof(AiAgentConfig.DefaultReasoningEffort))
        {
            if (_selectedThread is null)
            {
                UpdateProviderSelection();
                if (_useDefaultPermission)
                    SetPermissionMode(PermissionModes.First(item => item.Mode == _host.DefaultPermissionMode));
            }
        }
        else if (args.PropertyName is not (nameof(AiAgentConfig.EnableNotifications) or nameof(AiAgentConfig.EnableNotificationSound)))
        {
            _ = RefreshProvidersAsync();
        }
    });

    private async Task RefreshProvidersAsync()
    {
        var cancellation = new CancellationDisposable();
        var ct = cancellation.Token;
        _providerProbe.Disposable = cancellation;
        try
        {
            var providers = _host.GetProviders();
            var probes = await Task.Run(() => Task.WhenAll(providers.Select(provider =>
                _host.Runtime.ProbeProviderAsync(provider, ct))), ct);
            if (ct.IsCancellationRequested)
                return;

            _detectedProviders = probes.Select(probe =>
                new AgentProviderChoice(probe.Provider, probe.IsAvailable, probe.CliVersion,
                    AgentDiagnosticClassifier.FromProbe(probe))).ToArray();
            InvalidateModelCatalogs();
            UpdateProviderSelection();
            UpdateThreadRows();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                ReportError(ex);
        }
    }

    private void UpdateProviderSelection()
    {
        string? selectedId = _selectedThread?.ProviderId ?? _draftProviderId;
        var selected = _detectedProviders.FirstOrDefault(provider => provider.Descriptor.Id == selectedId);
        if (selected is null && _selectedThread is { } thread)
        {
            var provider = new AgentProviderDescriptor(thread.ProviderId, AgentProviderKind.Acp, thread.ProviderId, "");
            selected = new AgentProviderChoice(provider, false, null,
                AgentDiagnosticClassifier.FromProbe(new AgentProviderProbeResult(provider, null, null, AgentStrings.ProviderUnavailable)));
            Providers = [.. _detectedProviders, selected];
        }
        else
        {
            Providers = _detectedProviders;
        }

        if (selected is null)
        {
            _draftProviderId = null;
            selected = _detectedProviders.FirstOrDefault(provider => provider.Descriptor.Id == _host.DefaultProviderId)
                ?? _detectedProviders.FirstOrDefault(provider => provider.IsAvailable) ?? _detectedProviders.FirstOrDefault();
        }
        SelectedProvider = selected;
        Notify(nameof(CanChooseModel));
        RefreshModelSelection();
    }
}
