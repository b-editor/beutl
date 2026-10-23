using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Beutl.AgentToolkit.Common;
using Beutl.AgentToolkit.Installation;
using Beutl.Configuration;
using Beutl.Language;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AiAgentSettingsPageViewModel : IDisposable
{
    public const string CustomAgentId = "custom";

    private readonly AiAgentConfig _config;
    private readonly CompositeDisposable _disposables = [];

    public AiAgentSettingsPageViewModel(AiAgentConfig? config = null)
    {
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

        AgentChoiceItem initialAgent = AgentChoices.FirstOrDefault(a => a.Id == _config.AgentId.Value)
                                       ?? AgentChoices[0];
        InstallScopeItem initialScope = ScopeChoices.FirstOrDefault(s => s.Scope.ToString() == _config.InstallScope.Value)
                                        ?? ScopeChoices[0];

        SelectedAgent = new ReactivePropertySlim<AgentChoiceItem>(initialAgent).DisposeWith(_disposables);
        SelectedScope = new ReactivePropertySlim<InstallScopeItem>(initialScope).DisposeWith(_disposables);
        ProjectRoot = new ReactivePropertySlim<string>(_config.ProjectRoot.Value).DisposeWith(_disposables);
        SkillsDirectory = new ReactivePropertySlim<string>(_config.SkillsDirectory.Value).DisposeWith(_disposables);
        SubagentsDirectory = new ReactivePropertySlim<string>(_config.SubagentsDirectory.Value).DisposeWith(_disposables);
        McpConfigFileName = new ReactivePropertySlim<string>(_config.McpConfigFileName.Value).DisposeWith(_disposables);
        McpServersPropertyName = new ReactivePropertySlim<string>(_config.McpServersPropertyName.Value).DisposeWith(_disposables);
        // Prefer the user's saved override; fall back to the detected launcher so a fresh view model
        // (reopen, or the reinstall update-prompt path) does not silently revert a customized command.
        McpCommand = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.StdioCommand.Value, command?.Command ?? "")).DisposeWith(_disposables);
        McpArguments = new ReactivePropertySlim<string>(
            FirstNonEmpty(
                _config.StdioArguments.Value,
                command is null ? "" : string.Join(Environment.NewLine, command.Arguments)))
            .DisposeWith(_disposables);
        InstallSkills = new ReactivePropertySlim<bool>(_config.InstallSkills.Value).DisposeWith(_disposables);
        InstallSubagents = new ReactivePropertySlim<bool>(_config.InstallSubagents.Value).DisposeWith(_disposables);
        InstallMcp = new ReactivePropertySlim<bool>(_config.InstallMcp.Value).DisposeWith(_disposables);
        FollowLiveMcpEdits = new ReactivePropertySlim<bool>(_config.FollowLiveMcpEdits.Value).DisposeWith(_disposables);
        IsScopeSelectable = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        IsProjectFolderVisible = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanInstallSubagents = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanInstallMcp = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanInstallMcpServer = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        CanCustomizeMcpConfig = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        McpUnavailableMessage = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsMcpCommandMissing = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        ResolvedSkillsPath = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        ResolvedSubagentsPath = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        ResolvedMcpConfigPath = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        Status = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        ManualMcpSnippet = new ReactivePropertySlim<string>("").DisposeWith(_disposables);
        IsInstalling = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        HasInstalledFiles = new ReactivePropertySlim<bool>().DisposeWith(_disposables);

        RecomputeTargets();
        SubscribeRecompute();
        PersistOnChange();

        Install = new AsyncReactiveCommand()
            .WithSubscribe(InstallAsync)
            .DisposeWith(_disposables);
    }

    public IReadOnlyList<AgentChoiceItem> AgentChoices { get; }

    public IReadOnlyList<InstallScopeItem> ScopeChoices { get; }

    public ReactivePropertySlim<AgentChoiceItem> SelectedAgent { get; }

    public ReactivePropertySlim<InstallScopeItem> SelectedScope { get; }

    public ReactivePropertySlim<string> ProjectRoot { get; }

    public ReactivePropertySlim<string> SkillsDirectory { get; }

    public ReactivePropertySlim<string> SubagentsDirectory { get; }

    public ReactivePropertySlim<string> McpConfigFileName { get; }

    public ReactivePropertySlim<string> McpServersPropertyName { get; }

    public ReactivePropertySlim<string> McpCommand { get; }

    public ReactivePropertySlim<string> McpArguments { get; }

    public ReactivePropertySlim<bool> InstallSkills { get; }

    public ReactivePropertySlim<bool> InstallSubagents { get; }

    public ReactivePropertySlim<bool> InstallMcp { get; }

    public ReactivePropertySlim<bool> FollowLiveMcpEdits { get; }

    public ReactivePropertySlim<bool> IsScopeSelectable { get; }

    public ReactivePropertySlim<bool> IsProjectFolderVisible { get; }

    public ReactivePropertySlim<bool> CanInstallSubagents { get; }

    // The agent's MCP registry can be written (config file) or registered (CLI).
    public ReactivePropertySlim<bool> CanInstallMcp { get; }

    // CanInstallMcp and the server binary was found (or a command was entered under Advanced).
    public ReactivePropertySlim<bool> CanInstallMcpServer { get; }

    public ReactivePropertySlim<bool> CanCustomizeMcpConfig { get; }

    public ReactivePropertySlim<string> McpUnavailableMessage { get; }

    public ReactivePropertySlim<bool> IsMcpCommandMissing { get; }

    public ReactivePropertySlim<string> ResolvedSkillsPath { get; }

    public ReactivePropertySlim<string> ResolvedSubagentsPath { get; }

    public ReactivePropertySlim<string> ResolvedMcpConfigPath { get; }

    public ReactivePropertySlim<string> Status { get; }

    // The MCP entry to add by hand when the agent's JSON configuration has comments, which Beutl does not
    // rewrite; empty otherwise.
    public ReactivePropertySlim<string> ManualMcpSnippet { get; }

    public ReactivePropertySlim<bool> IsInstalling { get; }

    public ReactivePropertySlim<bool> HasInstalledFiles { get; }

    public ObservableCollection<string> InstalledFiles { get; } = [];

    public AsyncReactiveCommand Install { get; }

    private sealed record ResolvedTargets(
        string Root,
        string SkillsDirectory,
        string? SubagentsDirectory,
        SubagentFileFormat SubagentFormat,
        string? McpConfigFileName,
        string McpConfigRoot,
        McpConfigFormat McpConfigFormat,
        string? McpConfigurationError,
        string McpServersPropertyName,
        string? McpTypeValue,
        bool UseCliForMcp);

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
        bool codex = mcp?.Format == McpConfigFormat.CodexToml;
        // Codex's path and root table are fixed. Shared JSON overrides belong
        // to other agents and must not redirect a Codex installation.
        string? mcpFile = codex ? mcp!.ConfigFileName
            : !string.IsNullOrWhiteSpace(McpConfigFileName.Value)
            ? McpConfigFileName.Value
            : agent is null ? ".mcp.json" : mcp?.ConfigFileName;

        string mcpRoot = root;
        string? mcpError = null;
        if (codex
            && scope == AgentInstallScope.Global
            && Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } codexHome)
        {
            if (Path.IsPathFullyQualified(codexHome))
            {
                mcpRoot = Path.GetFullPath(codexHome);
                mcpFile = "config.toml";
            }
            else
            {
                mcpFile = null;
                mcpError = SettingsStrings.AiAgents_CodexHomeMustBeAbsolute;
            }
        }

        string mcpProperty = codex ? mcp!.ServersPropertyName : FirstNonEmpty(
            McpServersPropertyName.Value,
            mcp?.ServersPropertyName ?? "mcpServers");

        // A manual file override on an agent without a known MCP location omits the "type" key,
        // which most agents infer from "command".
        string? mcpType = mcp?.StdioTypeValue;

        bool useCli = mcpError is null && mcpFile is null
                      && AgentMcpCliCommands.SupportsStdio(SelectedAgent.Value.Id, scope);

        return new ResolvedTargets(
            root, skills, subagents, agent?.SubagentFormat ?? SubagentFileFormat.Markdown,
            mcpFile, mcpRoot, mcp?.Format ?? McpConfigFormat.Json, mcpError,
            mcpProperty, mcpType, useCli);
    }

    private McpCliCommand? BuildCliMcpCommand()
        => AgentMcpCliCommands.BuildStdio(
            SelectedAgent.Value.Id,
            SelectedScope.Value.Scope,
            AgentToolkitInstallOptions.DefaultServerName,
            McpCommand.Value,
            ParseArguments(McpArguments.Value),
            McpServerEnvironment());

    // The server discovers the editors of this profile through BEUTL_HOME and authenticates to
    // them itself, so the agent config never holds a process-specific URL or the bearer token.
    // Absolute, because the agent host starts the server from a working directory of its own.
    private static Dictionary<string, string> McpServerEnvironment()
        => new() { [BeutlEnvironment.HomeVariable] = Path.GetFullPath(BeutlEnvironment.GetHomeDirectoryPath()) };

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
        InstallMcp.Skip(1).Subscribe(_ => RecomputeTargets()).DisposeWith(_disposables);
    }

    private void RecomputeTargets()
    {
        ResolvedTargets targets = Resolve();
        bool custom = SelectedAgent.Value.Definition is null;

        IsScopeSelectable.Value = !custom;
        IsProjectFolderVisible.Value = custom || SelectedScope.Value.Scope == AgentInstallScope.Project;
        bool commandAvailable = !string.IsNullOrWhiteSpace(McpCommand.Value);
        CanInstallSubagents.Value = targets.SubagentsDirectory is not null;
        CanInstallMcp.Value = targets.McpConfigFileName is not null || targets.UseCliForMcp;
        CanCustomizeMcpConfig.Value = targets.McpConfigFormat != McpConfigFormat.CodexToml;
        McpUnavailableMessage.Value = targets.McpConfigurationError ?? SettingsStrings.AiAgents_McpManual;
        CanInstallMcpServer.Value = CanInstallMcp.Value && commandAvailable;
        IsMcpCommandMissing.Value = !commandAvailable;

        ResolvedSkillsPath.Value = DisplayPath(targets.Root, targets.SkillsDirectory);
        ResolvedSubagentsPath.Value = targets.SubagentsDirectory is null
            ? SettingsStrings.AiAgents_NotSupported
            : DisplayPath(targets.Root, targets.SubagentsDirectory);
        ResolvedMcpConfigPath.Value = targets.McpConfigurationError ?? (targets.McpConfigFileName is not null
            ? DisplayPath(targets.McpConfigRoot, targets.McpConfigFileName)
            : targets.UseCliForMcp && BuildCliPreview(commandAvailable) is { } cliPreview
                ? cliPreview
                : targets.UseCliForMcp ? "—" : SettingsStrings.AiAgents_NotSupported);
    }

    private string? BuildCliPreview(bool commandAvailable)
    {
        return InstallMcp.Value && commandAvailable && BuildCliMcpCommand() is { } command
            ? "$ " + command.ToDisplayString()
            : null;
    }

    private static string DisplayPath(string root, string relativePath)
    {
        return string.IsNullOrWhiteSpace(root) ? relativePath : Path.Combine(root, relativePath);
    }

    private void PersistOnChange()
    {
        SelectedAgent.Skip(1).Subscribe(v => _config.AgentId.Value = v.Id).DisposeWith(_disposables);
        SelectedScope.Skip(1).Subscribe(v => _config.InstallScope.Value = v.Scope.ToString()).DisposeWith(_disposables);
        ProjectRoot.Skip(1).Subscribe(v => _config.ProjectRoot.Value = v).DisposeWith(_disposables);
        SkillsDirectory.Skip(1).Subscribe(v => _config.SkillsDirectory.Value = v).DisposeWith(_disposables);
        SubagentsDirectory.Skip(1).Subscribe(v => _config.SubagentsDirectory.Value = v).DisposeWith(_disposables);
        McpConfigFileName.Skip(1).Subscribe(v => _config.McpConfigFileName.Value = v).DisposeWith(_disposables);
        McpServersPropertyName.Skip(1).Subscribe(v => _config.McpServersPropertyName.Value = v).DisposeWith(_disposables);
        InstallSkills.Skip(1).Subscribe(v => _config.InstallSkills.Value = v).DisposeWith(_disposables);
        InstallSubagents.Skip(1).Subscribe(v => _config.InstallSubagents.Value = v).DisposeWith(_disposables);
        InstallMcp.Skip(1).Subscribe(v => _config.InstallMcp.Value = v).DisposeWith(_disposables);
        FollowLiveMcpEdits.Skip(1).Subscribe(v => _config.FollowLiveMcpEdits.Value = v).DisposeWith(_disposables);
        McpCommand.Skip(1).Subscribe(v => _config.StdioCommand.Value = v).DisposeWith(_disposables);
        McpArguments.Skip(1).Subscribe(v => _config.StdioArguments.Value = v).DisposeWith(_disposables);
    }

    public async Task InstallAsync()
    {
        try
        {
            IsInstalling.Value = true;
            Status.Value = "";
            ManualMcpSnippet.Value = "";
            InstalledFiles.Clear();
            HasInstalledFiles.Value = false;
            RecomputeTargets();

            ResolvedTargets targets = Resolve();
            bool installSubagents = InstallSubagents.Value && targets.SubagentsDirectory is not null;
            string? mcpError = InstallMcp.Value ? targets.McpConfigurationError : null;
            if (mcpError is not null && !InstallSkills.Value && !installSubagents)
            {
                Status.Value = mcpError;
                return;
            }

            if (string.IsNullOrWhiteSpace(targets.Root))
            {
                Status.Value = SettingsStrings.AiAgents_ProjectFolderMissing;
                return;
            }

            bool canWriteMcp = targets.McpConfigFileName is not null;
            bool commandAvailable = !string.IsNullOrWhiteSpace(McpCommand.Value);
            bool installMcp = InstallMcp.Value && canWriteMcp && commandAvailable;

            IReadOnlyList<AgentToolkitAsset> assets = BundledAgentToolkitAssets.Load();
            // The recorded hashes tell an untouched earlier install from a file the user edited.
            AgentToolkitInstallManifest? previous =
                AgentToolkitInstallManifestStore.Load(AgentToolkitInstallManifestStore.GetDefaultPath());
            AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
                BuildInstallOptions(targets, installSubagents, installMcp) with { PreviousFiles = previous?.Files ?? [] },
                assets);

            foreach (string file in result.InstalledFiles)
            {
                InstalledFiles.Add(file);
            }

            foreach (BackedUpFile file in result.BackedUpFiles)
            {
                InstalledFiles.Add("backed up: " + file.BackupPath);
            }

            ManualMcpSnippet.Value = result.ManualMcpEdit?.Snippet ?? "";

            var cliErrors = new List<string>();
            bool cliRegistered = targets.UseCliForMcp
                                 && await RegisterMcpThroughCliAsync(cliErrors).ConfigureAwait(true);

            foreach (string file in UpdateInstallManifest(targets, result, assets, installSubagents, result.InstalledMcp || cliRegistered))
            {
                InstalledFiles.Add("removed: " + file);
            }

            HasInstalledFiles.Value = InstalledFiles.Count > 0;
            PublishInstallStatus(mcpError, cliErrors, result);
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

    private AgentToolkitInstallOptions BuildInstallOptions(
        ResolvedTargets targets,
        bool installSubagents,
        bool installMcp)
    {
        return new AgentToolkitInstallOptions
        {
            AgentRoot = targets.Root,
            SkillsDirectory = targets.SkillsDirectory,
            SubagentsDirectory = targets.SubagentsDirectory ?? "agents",
            SubagentFormat = targets.SubagentFormat,
            InstallSkills = InstallSkills.Value,
            InstallSubagents = installSubagents,
            InstallMcp = installMcp,
            McpConfigFileName = targets.McpConfigFileName ?? ".mcp.json",
            McpConfigRoot = targets.McpConfigRoot,
            McpConfigFormat = targets.McpConfigFormat,
            McpServersPropertyName = targets.McpServersPropertyName,
            McpTypeValue = targets.McpTypeValue,
            McpCommand = McpCommand.Value,
            McpArguments = ParseArguments(McpArguments.Value),
            McpEnvironment = McpServerEnvironment(),
        };
    }

    private void PublishInstallStatus(string? mcpError, List<string> cliErrors, AgentToolkitInstallResult result)
    {
        Status.Value = string.Format(SettingsStrings.AiAgents_InstallCompleted, InstalledFiles.Count);
        if (result.BackedUpFiles.Count > 0)
            Status.Value += Environment.NewLine + string.Format(SettingsStrings.AiAgents_EditedFilesBackedUp, result.BackedUpFiles.Count);
        if (result.ManualMcpEdit is { } manual)
        {
            Status.Value += Environment.NewLine + string.Format(SettingsStrings.AiAgents_McpConfigHasComments, manual.ConfigPath);
            if (manual.ObsoleteEntries.Count > 0)
            {
                Status.Value += Environment.NewLine + string.Format(
                    SettingsStrings.AiAgents_McpConfigObsoleteEntries,
                    string.Join(", ", manual.ObsoleteEntries.Select(name => $"\"{name}\"")));
            }
        }

        if (mcpError is not null)
            Status.Value += Environment.NewLine + mcpError;
        if (cliErrors.Count > 0)
        {
            Status.Value += Environment.NewLine + string.Format(
                SettingsStrings.AiAgents_CliFailed,
                string.Join(Environment.NewLine, cliErrors));
        }
    }

    // Remove stale files from the previous install, but only under the components installed this run;
    // carry other records over for a later reinstall.
    private IReadOnlyList<string> UpdateInstallManifest(
        ResolvedTargets targets,
        AgentToolkitInstallResult result,
        IReadOnlyList<AgentToolkitAsset> assets,
        bool installedSubagents,
        bool installedMcp)
    {
        string manifestPath = AgentToolkitInstallManifestStore.GetDefaultPath();
        AgentToolkitInstallManifest? previous = AgentToolkitInstallManifestStore.Load(manifestPath);

        HashSet<string> currentPaths = result.AssetFileRecords
            .Select(r => r.Path)
            .ToHashSet(AgentToolkitInstallManifestStore.PathComparer);

        IReadOnlyList<string> removed = [];
        var carriedOver = new List<InstalledFileRecord>();
        if (previous is not null)
        {
            var pruneRoots = new List<string>();
            string root = Path.GetFullPath(targets.Root);
            if (InstallSkills.Value)
            {
                pruneRoots.Add(Path.Combine(root, targets.SkillsDirectory));
            }

            if (installedSubagents)
            {
                pruneRoots.Add(Path.Combine(root, targets.SubagentsDirectory!));
            }

            InstalledFileRecord[] inScope = previous.Files
                .Where(f => pruneRoots.Any(r => IsUnder(f.Path, r)))
                .ToArray();
            removed = AgentToolkitInstallManifestStore.RemoveStaleFiles(inScope, currentPaths);

            var removedSet = removed.ToHashSet(AgentToolkitInstallManifestStore.PathComparer);
            carriedOver.AddRange(previous.Files.Where(f =>
                !currentPaths.Contains(f.Path)
                && !removedSet.Contains(f.Path)
                && File.Exists(f.Path)));
        }

        // Advance the MCP layout only when this install replaced the entry or there is nothing to migrate;
        // otherwise keep the old layout so the notice returns.
        bool mcpSettled = installedMcp
                          || !InstallMcp.Value
                          || (targets.McpConfigurationError is null
                              && targets.McpConfigFileName is null && !targets.UseCliForMcp);
        int mcpLayout = mcpSettled
            ? AgentToolkitInstallManifest.CurrentMcpLayout
            : previous?.McpLayout ?? 0;
        AgentToolkitInstallManifestStore.Save(manifestPath, new AgentToolkitInstallManifest(
            AgentToolkitInstallManifestStore.ComputeAssetsHash(assets),
            [.. result.AssetFileRecords, .. carriedOver],
            mcpLayout));
        return removed;
    }

    private static bool IsUnder(string path, string root)
    {
        // Resolve both through symlinks before comparing: a lexical check would treat a recorded path
        // under a since-retargeted symlink as in-scope, and RemoveStaleFiles would then delete the
        // link's outside target.
        string fullRoot = FilePathComparison.ResolveCanonicalPath(Path.GetFullPath(root));
        string fullPath = FilePathComparison.ResolveCanonicalPath(Path.GetFullPath(path));
        return fullPath.StartsWith(
            fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    // Returns whether the agent's CLI now holds the current entry.
    private async Task<bool> RegisterMcpThroughCliAsync(List<string> errors)
    {
        if (!InstallMcp.Value
            || string.IsNullOrWhiteSpace(McpCommand.Value)
            || BuildCliMcpCommand() is not { } addCommand)
        {
            return false;
        }

        string agentId = SelectedAgent.Value.Id;
        AgentInstallScope scope = SelectedScope.Value.Scope;
        // Best-effort remove keeps re-installs idempotent; `mcp add` fails on
        // an existing server name.
        if (AgentMcpCliCommands.BuildRemove(agentId, scope, AgentToolkitInstallOptions.DefaultServerName) is { } removeCommand)
        {
            await McpCliRunner.RunAsync(removeCommand).ConfigureAwait(true);
        }

        // Earlier versions also registered a URL entry carrying the live token; drop it the same way.
        if (AgentMcpCliCommands.BuildRemove(agentId, scope, AgentToolkitInstallOptions.LegacyLiveServerName) is { } removeLegacyCommand)
        {
            await McpCliRunner.RunAsync(removeLegacyCommand).ConfigureAwait(true);
        }

        McpCliResult result = await McpCliRunner.RunAsync(addCommand).ConfigureAwait(true);
        if (!result.Success)
        {
            errors.Add($"{addCommand.ToDisplayString()}: {result.Output}");
        }

        return result.Success;
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

    public void Dispose()
    {
        _disposables.Dispose();
    }
}

public sealed record AgentChoiceItem(string Id, string DisplayName, AgentDefinition? Definition);

public sealed record InstallScopeItem(AgentInstallScope Scope, string DisplayName);
