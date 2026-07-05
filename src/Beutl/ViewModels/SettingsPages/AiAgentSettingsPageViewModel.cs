using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Installation;
using Beutl.Configuration;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AiAgentSettingsPageViewModel : IDisposable
{
    public const string CustomAgentId = "custom";

    private readonly AgentHostEndpoint _agentHostEndpoint;
    private readonly AiAgentConfig _config;
    private readonly CompositeDisposable _disposables = [];

    public AiAgentSettingsPageViewModel(AgentHostEndpoint agentHostEndpoint, AiAgentConfig? config = null)
    {
        _agentHostEndpoint = agentHostEndpoint;
        _config = config ?? GlobalConfiguration.Instance.AiAgentConfig;

        AgentToolkitMcpServerCommand? command = AgentToolkitMcpServerLocator.ResolveDefault();

        AgentChoices =
        [
            .. AgentCatalog.Agents.Select(a => new AgentChoiceItem(a.Id, a.DisplayName, a)),
            new AgentChoiceItem(CustomAgentId, SettingsStrings.AiAgents_Agent_Custom, null),
        ];
        ScopeChoices =
        [
            new InstallScopeItem(AgentInstallScope.Global, SettingsStrings.AiAgents_Scope_Global),
            new InstallScopeItem(AgentInstallScope.Project, SettingsStrings.AiAgents_Scope_Project),
        ];

        AgentChoiceItem initialAgent = AgentChoices.FirstOrDefault(a => a.Id == _config.AgentId)
                                       ?? AgentChoices[0];
        InstallScopeItem initialScope = ScopeChoices.FirstOrDefault(s => s.Scope.ToString() == _config.InstallScope)
                                        ?? ScopeChoices[0];

        SelectedAgent = new ReactivePropertySlim<AgentChoiceItem>(initialAgent).DisposeWith(_disposables);
        SelectedScope = new ReactivePropertySlim<InstallScopeItem>(initialScope).DisposeWith(_disposables);
        ProjectRoot = new ReactivePropertySlim<string>(_config.ProjectRoot).DisposeWith(_disposables);
        WorkspaceRoot = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.WorkspaceRoot, GetDefaultWorkspaceRoot())).DisposeWith(_disposables);
        SkillsDirectory = new ReactivePropertySlim<string>(_config.SkillsDirectory).DisposeWith(_disposables);
        SubagentsDirectory = new ReactivePropertySlim<string>(_config.SubagentsDirectory).DisposeWith(_disposables);
        McpConfigFileName = new ReactivePropertySlim<string>(_config.McpConfigFileName).DisposeWith(_disposables);
        McpServersPropertyName = new ReactivePropertySlim<string>(_config.McpServersPropertyName).DisposeWith(_disposables);
        McpCommand = new ReactivePropertySlim<string>(command?.Command ?? "").DisposeWith(_disposables);
        McpArguments = new ReactivePropertySlim<string>(
            command is null ? "" : string.Join(Environment.NewLine, command.Arguments))
            .DisposeWith(_disposables);
        InstallSkills = new ReactivePropertySlim<bool>(_config.InstallSkills).DisposeWith(_disposables);
        InstallSubagents = new ReactivePropertySlim<bool>(_config.InstallSubagents).DisposeWith(_disposables);
        InstallStdioMcp = new ReactivePropertySlim<bool>(_config.InstallStdioMcp).DisposeWith(_disposables);
        InstallLiveMcp = new ReactivePropertySlim<bool>(_config.InstallLiveMcp).DisposeWith(_disposables);
        LiveMcpUrl = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        LiveMcpAuthHeader = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsLiveMcpAvailable = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        IsCustomAgent = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        IsScopeSelectable = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        IsProjectFolderVisible = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanInstallSubagents = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanInstallMcp = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanInstallStdioMcp = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanInstallLiveMcp = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        IsStdioCommandMissing = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        ResolvedSkillsPath = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        ResolvedSubagentsPath = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        ResolvedMcpConfigPath = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        Status = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsInstalling = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        HasInstalledFiles = new ReactivePropertySlim<bool>().DisposeWith(_disposables);

        RefreshLiveEndpoint();
        RecomputeTargets();
        SubscribeRecompute();
        PersistOnChange();

        Install = new AsyncReactiveCommand()
            .WithSubscribe(InstallAsync)
            .DisposeWith(_disposables);
        RefreshLiveMcp = new ReactiveCommand()
            .WithSubscribe(() =>
            {
                RefreshLiveEndpoint();
                RecomputeTargets();
            })
            .DisposeWith(_disposables);
    }

    public IReadOnlyList<AgentChoiceItem> AgentChoices { get; }

    public IReadOnlyList<InstallScopeItem> ScopeChoices { get; }

    public ReactivePropertySlim<AgentChoiceItem> SelectedAgent { get; }

    public ReactivePropertySlim<InstallScopeItem> SelectedScope { get; }

    public ReactivePropertySlim<string> ProjectRoot { get; }

    public ReactivePropertySlim<string> WorkspaceRoot { get; }

    public ReactivePropertySlim<string> SkillsDirectory { get; }

    public ReactivePropertySlim<string> SubagentsDirectory { get; }

    public ReactivePropertySlim<string> McpConfigFileName { get; }

    public ReactivePropertySlim<string> McpServersPropertyName { get; }

    public ReactivePropertySlim<string> McpCommand { get; }

    public ReactivePropertySlim<string> McpArguments { get; }

    public ReactivePropertySlim<bool> InstallSkills { get; }

    public ReactivePropertySlim<bool> InstallSubagents { get; }

    public ReactivePropertySlim<bool> InstallStdioMcp { get; }

    public ReactivePropertySlim<bool> InstallLiveMcp { get; }

    public ReactivePropertySlim<string> LiveMcpUrl { get; }

    public ReactivePropertySlim<string> LiveMcpAuthHeader { get; }

    public ReactivePropertySlim<bool> IsLiveMcpAvailable { get; }

    public ReactivePropertySlim<bool> IsCustomAgent { get; }

    public ReactivePropertySlim<bool> IsScopeSelectable { get; }

    public ReactivePropertySlim<bool> IsProjectFolderVisible { get; }

    public ReactivePropertySlim<bool> CanInstallSubagents { get; }

    public ReactivePropertySlim<bool> CanInstallMcp { get; }

    public ReactivePropertySlim<bool> CanInstallStdioMcp { get; }

    public ReactivePropertySlim<bool> CanInstallLiveMcp { get; }

    public ReactivePropertySlim<bool> IsStdioCommandMissing { get; }

    public ReactivePropertySlim<string> ResolvedSkillsPath { get; }

    public ReactivePropertySlim<string> ResolvedSubagentsPath { get; }

    public ReactivePropertySlim<string> ResolvedMcpConfigPath { get; }

    public ReactivePropertySlim<string> Status { get; }

    public ReactivePropertySlim<bool> IsInstalling { get; }

    public ReactivePropertySlim<bool> HasInstalledFiles { get; }

    public ObservableCollection<string> InstalledFiles { get; } = [];

    public AsyncReactiveCommand Install { get; }

    public ReactiveCommand RefreshLiveMcp { get; }

    private sealed record ResolvedTargets(
        string Root,
        string SkillsDirectory,
        string? SubagentsDirectory,
        SubagentFileFormat SubagentFormat,
        string? McpConfigFileName,
        string McpServersPropertyName,
        string? StdioTypeValue,
        string? LiveUrlPropertyName,
        string? LiveTypeValue,
        bool UseCliForMcp,
        bool CliSupportsRemote);

    private ResolvedTargets Resolve()
    {
        AgentDefinition? agent = SelectedAgent.Value.Definition;
        AgentInstallScope scope = SelectedScope.Value.Scope;

        string root = agent is not null && scope == AgentInstallScope.Global
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : ProjectRoot.Value;

        string skills = FirstNonEmpty(
            SkillsDirectory.Value,
            agent?.SkillsDirectory(scope) ?? "skills");

        string? subagents = !string.IsNullOrWhiteSpace(SubagentsDirectory.Value)
            ? SubagentsDirectory.Value
            : agent is null ? "agents" : agent.SubagentsDirectory(scope);

        AgentMcpLocation? mcp = agent?.Mcp(scope);
        string? mcpFile = !string.IsNullOrWhiteSpace(McpConfigFileName.Value)
            ? McpConfigFileName.Value
            : agent is null ? ".mcp.json" : mcp?.ConfigFileName;

        string mcpProperty = FirstNonEmpty(
            McpServersPropertyName.Value,
            mcp?.ServersPropertyName ?? "mcpServers");

        // A manual file override on an agent without a known MCP location
        // falls back to the ecosystem-standard entry shapes.
        string? stdioType = mcp?.StdioTypeValue;
        string? liveUrlProperty = mcp is null ? "url" : mcp.RemoteUrlPropertyName;
        string? liveType = mcp is null ? "http" : mcp.RemoteTypeValue;

        bool useCli = mcpFile is null
                      && AgentMcpCliCommands.SupportsStdio(SelectedAgent.Value.Id, scope);
        bool cliRemote = useCli
                         && AgentMcpCliCommands.SupportsRemote(SelectedAgent.Value.Id, scope);

        return new ResolvedTargets(
            root, skills, subagents, agent?.SubagentFormat ?? SubagentFileFormat.Markdown,
            mcpFile, mcpProperty, stdioType, liveUrlProperty, liveType, useCli, cliRemote);
    }

    private McpCliCommand? BuildCliStdioCommand()
    {
        var environment = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(WorkspaceRoot.Value))
        {
            environment["BEUTL_WORKSPACE"] = Path.GetFullPath(WorkspaceRoot.Value);
        }

        return AgentMcpCliCommands.BuildStdio(
            SelectedAgent.Value.Id,
            SelectedScope.Value.Scope,
            AgentToolkitInstallOptions.DefaultStdioServerName,
            McpCommand.Value,
            ParseArguments(McpArguments.Value),
            environment);
    }

    private void SubscribeRecompute()
    {
        SelectedAgent.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        SelectedScope.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        ProjectRoot.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        SkillsDirectory.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        SubagentsDirectory.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        McpConfigFileName.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        McpServersPropertyName.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        McpCommand.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        McpArguments.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        WorkspaceRoot.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        InstallStdioMcp.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
        InstallLiveMcp.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
    }

    private void RecomputeTargets()
    {
        ResolvedTargets targets = Resolve();
        bool custom = SelectedAgent.Value.Definition is null;

        IsCustomAgent.Value = custom;
        IsScopeSelectable.Value = !custom;
        IsProjectFolderVisible.Value = custom || SelectedScope.Value.Scope == AgentInstallScope.Project;
        bool stdioCommandAvailable = !string.IsNullOrWhiteSpace(McpCommand.Value);
        CanInstallSubagents.Value = targets.SubagentsDirectory is not null;
        CanInstallMcp.Value = targets.McpConfigFileName is not null || targets.UseCliForMcp;
        CanInstallStdioMcp.Value = CanInstallMcp.Value && stdioCommandAvailable;
        IsStdioCommandMissing.Value = !stdioCommandAvailable;
        CanInstallLiveMcp.Value = IsLiveMcpAvailable.Value
                                  && ((targets.McpConfigFileName is not null && targets.LiveUrlPropertyName is not null)
                                      || targets.CliSupportsRemote);

        ResolvedSkillsPath.Value = DisplayPath(targets.Root, targets.SkillsDirectory);
        ResolvedSubagentsPath.Value = targets.SubagentsDirectory is null
            ? SettingsStrings.AiAgents_NotSupported
            : DisplayPath(targets.Root, targets.SubagentsDirectory);
        ResolvedMcpConfigPath.Value = targets.McpConfigFileName is not null
            ? DisplayPath(targets.Root, targets.McpConfigFileName)
            : targets.UseCliForMcp && BuildCliPreview(targets) is { } cliPreview
                ? cliPreview
                : SettingsStrings.AiAgents_NotSupported;
    }

    private string? BuildCliPreview(ResolvedTargets targets)
    {
        bool stdioCommandAvailable = !string.IsNullOrWhiteSpace(McpCommand.Value);
        var lines = new List<string>();
        if (InstallStdioMcp.Value && stdioCommandAvailable && BuildCliStdioCommand() is { } stdio)
        {
            lines.Add("$ " + stdio.ToDisplayString());
        }

        if (InstallLiveMcp.Value
            && targets.CliSupportsRemote
            && TryCreateLiveMcpUri() is { } liveUri
            && AgentMcpCliCommands.BuildRemote(
                SelectedAgent.Value.Id,
                SelectedScope.Value.Scope,
                AgentToolkitInstallOptions.DefaultLiveServerName,
                liveUri,
                BuildLiveMcpHeaders()) is { } remote)
        {
            lines.Add("$ " + remote.ToDisplayString());
        }

        if (lines.Count == 0 && stdioCommandAvailable && BuildCliStdioCommand() is { } fallback)
        {
            lines.Add("$ " + fallback.ToDisplayString());
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    private static string DisplayPath(string root, string relativePath)
    {
        return string.IsNullOrWhiteSpace(root) ? relativePath : Path.Combine(root, relativePath);
    }

    private void PersistOnChange()
    {
        SelectedAgent.Skip(1).Subscribe(v => _config.AgentId = v.Id).DisposeWith(_disposables);
        SelectedScope.Skip(1).Subscribe(v => _config.InstallScope = v.Scope.ToString()).DisposeWith(_disposables);
        ProjectRoot.Skip(1).Subscribe(v => _config.ProjectRoot = v).DisposeWith(_disposables);
        WorkspaceRoot.Skip(1).Subscribe(v => _config.WorkspaceRoot = v).DisposeWith(_disposables);
        SkillsDirectory.Skip(1).Subscribe(v => _config.SkillsDirectory = v).DisposeWith(_disposables);
        SubagentsDirectory.Skip(1).Subscribe(v => _config.SubagentsDirectory = v).DisposeWith(_disposables);
        McpConfigFileName.Skip(1).Subscribe(v => _config.McpConfigFileName = v).DisposeWith(_disposables);
        McpServersPropertyName.Skip(1).Subscribe(v => _config.McpServersPropertyName = v).DisposeWith(_disposables);
        InstallSkills.Skip(1).Subscribe(v => _config.InstallSkills = v).DisposeWith(_disposables);
        InstallSubagents.Skip(1).Subscribe(v => _config.InstallSubagents = v).DisposeWith(_disposables);
        InstallStdioMcp.Skip(1).Subscribe(v => _config.InstallStdioMcp = v).DisposeWith(_disposables);
        InstallLiveMcp.Skip(1).Subscribe(v => _config.InstallLiveMcp = v).DisposeWith(_disposables);
    }

    private async Task InstallAsync()
    {
        try
        {
            IsInstalling.Value = true;
            Status.Value = "";
            InstalledFiles.Clear();
            HasInstalledFiles.Value = false;
            RefreshLiveEndpoint();
            RecomputeTargets();

            ResolvedTargets targets = Resolve();
            if (string.IsNullOrWhiteSpace(targets.Root))
            {
                Status.Value = SettingsStrings.AiAgents_ProjectFolderMissing;
                return;
            }

            Uri? liveMcpUri = TryCreateLiveMcpUri();
            bool canWriteMcp = targets.McpConfigFileName is not null;
            bool installStdioMcp = InstallStdioMcp.Value
                                   && canWriteMcp
                                   && !string.IsNullOrWhiteSpace(McpCommand.Value);
            bool installLiveMcp = InstallLiveMcp.Value
                                  && canWriteMcp
                                  && targets.LiveUrlPropertyName is not null
                                  && liveMcpUri is not null;

            AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
                new AgentToolkitInstallOptions
                {
                    AgentRoot = targets.Root,
                    SkillsDirectory = targets.SkillsDirectory,
                    SubagentsDirectory = targets.SubagentsDirectory ?? "agents",
                    SubagentFormat = targets.SubagentFormat,
                    InstallSkills = InstallSkills.Value,
                    InstallSubagents = InstallSubagents.Value && targets.SubagentsDirectory is not null,
                    InstallStdioMcp = installStdioMcp,
                    InstallLiveMcp = installLiveMcp,
                    McpConfigFileName = targets.McpConfigFileName ?? ".mcp.json",
                    McpServersPropertyName = targets.McpServersPropertyName,
                    StdioMcpTypeValue = targets.StdioTypeValue,
                    LiveMcpUrlPropertyName = targets.LiveUrlPropertyName ?? "url",
                    LiveMcpTypeValue = targets.LiveTypeValue,
                    WorkspaceRoot = WorkspaceRoot.Value,
                    StdioMcpCommand = McpCommand.Value,
                    StdioMcpArguments = ParseArguments(McpArguments.Value),
                    LiveMcpUri = installLiveMcp ? liveMcpUri : null,
                    LiveMcpHeaders = BuildLiveMcpHeaders(),
                },
                BundledAgentToolkitAssets.Load());

            foreach (string file in result.InstalledFiles)
            {
                InstalledFiles.Add(file);
            }

            var cliErrors = new List<string>();
            if (targets.UseCliForMcp)
            {
                await RegisterMcpThroughCliAsync(targets, liveMcpUri, cliErrors).ConfigureAwait(true);
            }

            HasInstalledFiles.Value = InstalledFiles.Count > 0;
            Status.Value = string.Format(SettingsStrings.AiAgents_InstallCompleted, InstalledFiles.Count);
            if (cliErrors.Count > 0)
            {
                Status.Value += Environment.NewLine + string.Format(
                    SettingsStrings.AiAgents_CliFailed,
                    string.Join(Environment.NewLine, cliErrors));
            }
        }
        catch (Exception ex)
        {
            Status.Value = ex.Message;
        }
        finally
        {
            IsInstalling.Value = false;
        }
    }

    private async Task RegisterMcpThroughCliAsync(
        ResolvedTargets targets,
        Uri? liveMcpUri,
        List<string> errors)
    {
        string agentId = SelectedAgent.Value.Id;
        AgentInstallScope scope = SelectedScope.Value.Scope;

        if (InstallStdioMcp.Value
            && !string.IsNullOrWhiteSpace(McpCommand.Value)
            && BuildCliStdioCommand() is { } stdioCommand)
        {
            await RunCliRegistrationAsync(
                agentId, scope, AgentToolkitInstallOptions.DefaultStdioServerName, stdioCommand, errors)
                .ConfigureAwait(true);
        }

        if (InstallLiveMcp.Value
            && targets.CliSupportsRemote
            && liveMcpUri is not null
            && AgentMcpCliCommands.BuildRemote(
                agentId,
                scope,
                AgentToolkitInstallOptions.DefaultLiveServerName,
                liveMcpUri,
                BuildLiveMcpHeaders()) is { } remoteCommand)
        {
            await RunCliRegistrationAsync(
                agentId, scope, AgentToolkitInstallOptions.DefaultLiveServerName, remoteCommand, errors)
                .ConfigureAwait(true);
        }
    }

    private static async Task RunCliRegistrationAsync(
        string agentId,
        AgentInstallScope scope,
        string serverName,
        McpCliCommand addCommand,
        List<string> errors)
    {
        // Best-effort remove keeps re-installs idempotent; `mcp add` fails on
        // an existing server name.
        if (AgentMcpCliCommands.BuildRemove(agentId, scope, serverName) is { } removeCommand)
        {
            await McpCliRunner.RunAsync(removeCommand).ConfigureAwait(true);
        }

        McpCliResult result = await McpCliRunner.RunAsync(addCommand).ConfigureAwait(true);
        if (!result.Success)
        {
            errors.Add($"{addCommand.ToDisplayString()}: {result.Output}");
        }
    }

    private void RefreshLiveEndpoint()
    {
        Uri? uri = TryCreateLiveMcpUri();
        LiveMcpUrl.Value = uri?.ToString() ?? "";
        LiveMcpAuthHeader.Value = uri is null ? "" : "Authorization: Bearer " + _agentHostEndpoint.Token;
        IsLiveMcpAvailable.Value = uri is not null;
    }

    private Uri? TryCreateLiveMcpUri()
    {
        return _agentHostEndpoint.EndpointUri;
    }

    private Dictionary<string, string> BuildLiveMcpHeaders()
    {
        return new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer " + _agentHostEndpoint.Token,
        };
    }

    private static string FirstNonEmpty(string configured, string fallback)
    {
        return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
    }

    private static IReadOnlyList<string> ParseArguments(string text)
    {
        return text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static string GetDefaultWorkspaceRoot()
    {
        string? environment = Environment.GetEnvironmentVariable("BEUTL_WORKSPACE");
        if (!string.IsNullOrWhiteSpace(environment))
        {
            return environment;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return string.IsNullOrWhiteSpace(documents)
            ? Directory.GetCurrentDirectory()
            : documents;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

public sealed record AgentChoiceItem(string Id, string DisplayName, AgentDefinition? Definition);

public sealed record InstallScopeItem(AgentInstallScope Scope, string DisplayName);
