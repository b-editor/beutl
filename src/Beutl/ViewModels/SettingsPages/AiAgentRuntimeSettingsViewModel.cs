using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.Json;
using Avalonia.Threading;
using Beutl.AgentHost;
using Beutl.Agents;
using Beutl.AgentToolkit.Installation;
using Beutl.Configuration;
using Beutl.Language;
using Beutl.Logging;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AiAgentRuntimeSettingsViewModel : IDisposable
{
    private static readonly ILogger s_logger = Log.CreateLogger<AiAgentRuntimeSettingsViewModel>();
    private readonly AiAgentConfig _config;
    private readonly AgentBinaryLocator _locator = new();
    private readonly CompositeDisposable _disposables = [];
    private readonly SerialDisposable _probe = new();

    internal AiAgentRuntimeSettingsViewModel(AiAgentConfig config)
    {
        _config = config;
        _probe.DisposeWith(_disposables);
        DefaultModel = BindText(config.DefaultModel);
        ClaudeCodeExecutablePath = BindText(config.ClaudeCodeExecutablePath);
        CodexExecutablePath = BindText(config.CodexExecutablePath);
        ProviderChoices = new ReactivePropertySlim<IReadOnlyList<AgentRuntimeProviderOption>>([]).DisposeWith(_disposables);
        DefaultProvider = new ReactivePropertySlim<AgentRuntimeProviderOption?>().DisposeWith(_disposables);
        DefaultPermission = BindChoice(ReadDefaultPermission(), value => config.DefaultPermissionMode.Value = value.Mode.ToString());
        SendAction = BindChoice(ReadSendAction(), value => config.SteerOnSend.Value = value.Steer);
        ComposerSendKey = BindChoice(ReadComposerSendKey(), value => config.ComposerSendKey.Value = value.Key);
        ClaudeCodeStatus = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        CodexStatus = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        CustomProviders = new ReactivePropertySlim<IReadOnlyList<AgentCustomProviderItem>>([]).DisposeWith(_disposables);
        CustomProvidersError = new ReactivePropertySlim<string>("").DisposeWith(_disposables);
        NewProviderId = new ReactivePropertySlim<string>("").DisposeWith(_disposables);
        NewProviderName = new ReactivePropertySlim<string>("").DisposeWith(_disposables);
        NewProviderCommand = new ReactivePropertySlim<string>("").DisposeWith(_disposables);
        NewProviderArguments = new ReactivePropertySlim<string>("").DisposeWith(_disposables);
        NewProviderError = new ReactivePropertySlim<string>("").DisposeWith(_disposables);
        AddProvider = new ReactiveCommand().WithSubscribe(AddCustomProvider).DisposeWith(_disposables);
        RecheckProviders = new ReactiveCommand().WithSubscribe(config.RequestRuntimeProviderRefresh).DisposeWith(_disposables);

        ReloadProviders(updateChoices: true);
        DefaultProvider.Skip(1).OfType<AgentRuntimeProviderOption>()
            .Subscribe(value => config.DefaultRuntimeProviderId.Value = value.Id).DisposeWith(_disposables);
        config.RuntimeSettingsChanged += OnRuntimeSettingsChanged;
        Disposable.Create(() => config.RuntimeSettingsChanged -= OnRuntimeSettingsChanged).DisposeWith(_disposables);
    }

    public IReadOnlyList<AgentRuntimePermissionOption> PermissionChoices { get; } =
    [
        new(AgentPermissionMode.Supervised, AgentStrings.Supervised),
        new(AgentPermissionMode.AutoAcceptEdits, AgentStrings.AutoAcceptEdits),
        new(AgentPermissionMode.Auto, AgentStrings.Auto),
        new(AgentPermissionMode.FullAccess, AgentStrings.FullAccess)
    ];

    public ReactivePropertySlim<IReadOnlyList<AgentRuntimeProviderOption>> ProviderChoices { get; }
    public ReactivePropertySlim<AgentRuntimeProviderOption?> DefaultProvider { get; }
    public ReactivePropertySlim<AgentRuntimePermissionOption?> DefaultPermission { get; }
    public IReadOnlyList<AgentRuntimeSendOption> SendActions { get; } =
    [
        new(false, AgentStrings.QueueMessage),
        new(true, AgentStrings.SteerNow)
    ];
    public ReactivePropertySlim<AgentRuntimeSendOption?> SendAction { get; }
    public IReadOnlyList<AgentComposerSendKeyOption> ComposerSendKeys { get; } =
    [
        new(AgentComposerSendKey.Enter, SettingsStrings.AiAgents_RuntimeComposerSendKeyEnter),
        new(AgentComposerSendKey.CtrlEnter, OperatingSystem.IsMacOS()
            ? SettingsStrings.AiAgents_RuntimeComposerSendKeyCommandEnter
            : SettingsStrings.AiAgents_RuntimeComposerSendKeyCtrlEnter)
    ];
    public ReactivePropertySlim<AgentComposerSendKeyOption?> ComposerSendKey { get; }
    public IProperty<bool> EnableNotifications => _config.EnableNotifications;
    public IProperty<bool> EnableNotificationSound => _config.EnableNotificationSound;
    public ReactivePropertySlim<string> DefaultModel { get; }
    public ReactivePropertySlim<string> ClaudeCodeExecutablePath { get; }
    public ReactivePropertySlim<string> CodexExecutablePath { get; }
    public ReactivePropertySlim<string> ClaudeCodeStatus { get; }
    public ReactivePropertySlim<string> CodexStatus { get; }
    public ReactivePropertySlim<IReadOnlyList<AgentCustomProviderItem>> CustomProviders { get; }
    public ReactivePropertySlim<string> CustomProvidersError { get; }
    public ReactivePropertySlim<string> NewProviderId { get; }
    public ReactivePropertySlim<string> NewProviderName { get; }
    public ReactivePropertySlim<string> NewProviderCommand { get; }
    public ReactivePropertySlim<string> NewProviderArguments { get; }
    public ReactivePropertySlim<string> NewProviderError { get; }
    public ReactiveCommand AddProvider { get; }
    public ReactiveCommand RecheckProviders { get; }
    internal Task PendingProbe { get; private set; } = Task.CompletedTask;

    private ReactivePropertySlim<string> BindText(IProperty<string> setting)
    {
        var input = new ReactivePropertySlim<string>(setting.Value ?? "").DisposeWith(_disposables);
        input.Skip(1).Subscribe(value => setting.Value = value ?? "").DisposeWith(_disposables);
        setting.GetObservable().Skip(1).Subscribe(value => input.Value = value ?? "").DisposeWith(_disposables);
        return input;
    }

    private ReactivePropertySlim<TOption?> BindChoice<TOption>(TOption initial, Action<TOption> write)
        where TOption : class
    {
        var choice = new ReactivePropertySlim<TOption?>(initial).DisposeWith(_disposables);
        choice.Skip(1).OfType<TOption>().Subscribe(write).DisposeWith(_disposables);
        return choice;
    }

    private void OnRuntimeSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnRuntimeSettingsChanged(sender, args));
            return;
        }
        if (_disposables.IsDisposed)
            return;

        switch (args.PropertyName)
        {
            case nameof(AiAgentConfig.DefaultRuntimeProviderId):
                SelectDefaultProvider();
                break;
            case nameof(AiAgentConfig.DefaultPermissionMode):
                DefaultPermission.Value = ReadDefaultPermission();
                break;
            case nameof(AiAgentConfig.SteerOnSend):
                SendAction.Value = ReadSendAction();
                break;
            case nameof(AiAgentConfig.ComposerSendKey):
                ComposerSendKey.Value = ReadComposerSendKey();
                break;
            case nameof(AiAgentConfig.ClaudeCodeExecutablePath):
            case nameof(AiAgentConfig.CodexExecutablePath):
            case nameof(AiAgentConfig.CustomRuntimeProvidersJson):
            case null or "":
                ReloadProviders(updateChoices: args.PropertyName == nameof(AiAgentConfig.CustomRuntimeProvidersJson));
                break;
        }
    }

    private void SelectDefaultProvider()
    {
        var choices = ProviderChoices.Value;
        DefaultProvider.Value = choices.FirstOrDefault(option => option.Id == _config.DefaultRuntimeProviderId.Value?.Trim())
            ?? choices[0];
    }

    private AgentRuntimePermissionOption ReadDefaultPermission()
    {
        var mode = AgentRuntimeConfiguration.ParsePermissionMode(_config.DefaultPermissionMode.Value);
        return PermissionChoices.First(option => option.Mode == mode);
    }

    private AgentRuntimeSendOption ReadSendAction() => SendActions.First(option => option.Steer == _config.SteerOnSend.Value);

    private AgentComposerSendKeyOption ReadComposerSendKey() =>
        ComposerSendKeys.FirstOrDefault(option => option.Key == _config.ComposerSendKey.Value) ?? ComposerSendKeys[0];

    private bool TryReadCustomProviders(out IReadOnlyList<AgentCustomProviderConfig?> providers)
    {
        try
        {
            providers = AgentRuntimeConfiguration.ReadCustomProviders(_config.CustomRuntimeProvidersJson.Value);
            CustomProvidersError.Value = "";
            return true;
        }
        catch (JsonException)
        {
            providers = [];
            CustomProvidersError.Value = SettingsStrings.AiAgents_RuntimeInvalidProviders;
            return false;
        }
    }

    private void ReloadProviders(bool updateChoices)
    {
        TryReadCustomProviders(out var customProviders);
        var providers = AgentRuntimeConfiguration.GetProviders(_config, customProviders);
        if (updateChoices)
        {
            ProviderChoices.Value = [new("", SettingsStrings.AiAgents_RuntimeAuto),
                .. providers.Select(provider => new AgentRuntimeProviderOption(provider.Id, provider.DisplayName))];
            SelectDefaultProvider();
        }
        PendingProbe = ProbeProvidersAsync(providers);
    }

    private async Task ProbeProvidersAsync(IReadOnlyList<AgentProviderDescriptor> providers)
    {
        var cancellation = new CancellationDisposable();
        var ct = cancellation.Token;
        _probe.Disposable = cancellation;
        ApplyStatuses(providers, _ => SettingsStrings.AiAgents_RuntimeChecking);
        try
        {
            var probes = await Task.Run(() => Task.WhenAll(providers.Select(provider => _locator.ProbeAsync(provider, ct))), ct);
            if (!ct.IsCancellationRequested)
            {
                var statuses = probes.ToDictionary(probe => probe.Provider.Id, FormatProbeStatus);
                ApplyStatuses(providers, provider => statuses[provider.Id]);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested)
                return;
            s_logger.LogWarning(ex, "Could not locate agent runtime executables.");
            ApplyStatuses(providers, _ => SettingsStrings.AiAgents_RuntimeProbeFailed);
        }
    }

    private static string FormatProbeStatus(AgentProviderProbeResult probe)
    {
        if (probe.ExecutablePath is not { } path)
            return SettingsStrings.AiAgents_RuntimeNotFound;
        if (probe.IsOutdated)
            return string.Format(SettingsStrings.AiAgents_RuntimeOutdated, probe.CliVersion, probe.MinimumVersion, path);
        return probe.CliVersion is { } version
            ? string.Format(SettingsStrings.AiAgents_RuntimeAvailableVersion, version, path)
            : string.Format(SettingsStrings.AiAgents_RuntimeAvailable, path);
    }

    private void ApplyStatuses(IReadOnlyList<AgentProviderDescriptor> providers, Func<AgentProviderDescriptor, string> status)
    {
        ClaudeCodeStatus.Value = status(providers[0]);
        CodexStatus.Value = status(providers[1]);
        CustomProviders.Value = providers.Skip(2).Select(provider =>
            new AgentCustomProviderItem(provider.Id, provider.DisplayName, provider.Command,
                provider.Arguments is { Count: > 0 } arguments ? string.Join(Environment.NewLine, arguments) : "", status(provider)))
            .ToArray();
    }

    private void AddCustomProvider()
    {
        NewProviderError.Value = "";
        if (!TryReadCustomProviders(out var providers))
            return;

        string? id = NewProviderId.Value?.Trim();
        string? name = NewProviderName.Value?.Trim();
        string? command = NewProviderCommand.Value?.Trim();
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(command))
        {
            NewProviderError.Value = SettingsStrings.AiAgents_RuntimeRequiredFields;
            return;
        }
        if (id == AgentProviderDescriptor.ClaudeCode.Id || id == AgentProviderDescriptor.Codex.Id
            || providers.Any(provider => provider?.Id?.Trim() == id))
        {
            NewProviderError.Value = SettingsStrings.AiAgents_RuntimeDuplicateId;
            return;
        }

        var arguments = AgentToolkitMcpServerCommand.ParseArguments(NewProviderArguments.Value ?? "");
        _config.CustomRuntimeProvidersJson.Value = AgentRuntimeConfiguration.WriteCustomProviders(
            [.. providers, new AgentCustomProviderConfig(id, name, command, arguments)]);
        NewProviderId.Value = "";
        NewProviderName.Value = "";
        NewProviderCommand.Value = "";
        NewProviderArguments.Value = "";
    }

    public void RemoveProvider(string id)
    {
        if (!TryReadCustomProviders(out var providers))
            return;
        var remaining = providers.Where(provider => provider?.Id?.Trim() != id).ToArray();
        if (remaining.Length == providers.Count)
            return;

        if (_config.DefaultRuntimeProviderId.Value?.Trim() == id)
            _config.DefaultRuntimeProviderId.Value = "";
        _config.CustomRuntimeProvidersJson.Value = AgentRuntimeConfiguration.WriteCustomProviders(remaining);
    }

    public void Dispose() => _disposables.Dispose();
}

public sealed record AgentRuntimeProviderOption(string Id, string DisplayName);
public sealed record AgentRuntimePermissionOption(AgentPermissionMode Mode, string Label);
public sealed record AgentRuntimeSendOption(bool Steer, string Label);
public sealed record AgentComposerSendKeyOption(AgentComposerSendKey Key, string Label);
public sealed record AgentCustomProviderItem(string Id, string DisplayName, string Command, string Arguments, string Status);
