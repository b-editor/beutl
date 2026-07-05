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
    private readonly AgentHostEndpoint _agentHostEndpoint;
    private readonly AiAgentConfig _config;
    private readonly CompositeDisposable _disposables = [];

    public AiAgentSettingsPageViewModel(AgentHostEndpoint agentHostEndpoint, AiAgentConfig? config = null)
    {
        _agentHostEndpoint = agentHostEndpoint;
        _config = config ?? GlobalConfiguration.Instance.AiAgentConfig;

        AgentToolkitMcpServerCommand command = AgentToolkitMcpServerLocator.ResolveDefault();

        Layouts =
        [
            new AgentInstallLayoutItem(
                SettingsStrings.AiAgents_Layout_Generic,
                AgentToolkitInstallLayout.Generic,
                "skills",
                "agents"),
            new AgentInstallLayoutItem(
                SettingsStrings.AiAgents_Layout_ClaudeCode,
                AgentToolkitInstallLayout.ClaudeCode,
                Path.Combine(".claude", "skills"),
                Path.Combine(".claude", "agents")),
        ];
        AgentInstallLayoutItem initialLayout = Layouts.FirstOrDefault(
            i => i.Layout.ToString() == _config.InstallLayout) ?? Layouts[0];

        AgentRoot = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.AgentRoot, GetDefaultAgentRoot())).DisposeWith(_disposables);
        WorkspaceRoot = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.WorkspaceRoot, GetDefaultWorkspaceRoot())).DisposeWith(_disposables);
        SkillsDirectory = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.SkillsDirectory, initialLayout.SkillsDirectory)).DisposeWith(_disposables);
        SubagentsDirectory = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.SubagentsDirectory, initialLayout.SubagentsDirectory)).DisposeWith(_disposables);
        McpConfigFileName = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.McpConfigFileName, ".mcp.json")).DisposeWith(_disposables);
        McpServersPropertyName = new ReactivePropertySlim<string>(
            FirstNonEmpty(_config.McpServersPropertyName, "servers")).DisposeWith(_disposables);
        McpCommand = new ReactivePropertySlim<string>(command.Command).DisposeWith(_disposables);
        McpArguments = new ReactivePropertySlim<string>(string.Join(Environment.NewLine, command.Arguments))
            .DisposeWith(_disposables);
        InstallSkills = new ReactivePropertySlim<bool>(_config.InstallSkills).DisposeWith(_disposables);
        InstallSubagents = new ReactivePropertySlim<bool>(_config.InstallSubagents).DisposeWith(_disposables);
        InstallStdioMcp = new ReactivePropertySlim<bool>(_config.InstallStdioMcp).DisposeWith(_disposables);
        InstallLiveMcp = new ReactivePropertySlim<bool>(_config.InstallLiveMcp).DisposeWith(_disposables);
        LiveMcpUrl = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsLiveMcpAvailable = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        Status = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsInstalling = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        HasInstalledFiles = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        SelectedLayout = new ReactivePropertySlim<AgentInstallLayoutItem>(initialLayout).DisposeWith(_disposables);
        SelectedLayout.Skip(1).Subscribe(UpdatePresetDirectories).DisposeWith(_disposables);

        RefreshLiveEndpoint();
        PersistOnChange();

        Install = new AsyncReactiveCommand()
            .WithSubscribe(InstallAsync)
            .DisposeWith(_disposables);
        RefreshLiveMcp = new ReactiveCommand()
            .WithSubscribe(RefreshLiveEndpoint)
            .DisposeWith(_disposables);
    }

    public IReadOnlyList<AgentInstallLayoutItem> Layouts { get; }

    public ReactivePropertySlim<AgentInstallLayoutItem> SelectedLayout { get; }

    public ReactivePropertySlim<string> AgentRoot { get; }

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

    public ReactivePropertySlim<bool> IsLiveMcpAvailable { get; }

    public ReactivePropertySlim<string> Status { get; }

    public ReactivePropertySlim<bool> IsInstalling { get; }

    public ReactivePropertySlim<bool> HasInstalledFiles { get; }

    public ObservableCollection<string> InstalledFiles { get; } = [];

    public AsyncReactiveCommand Install { get; }

    public ReactiveCommand RefreshLiveMcp { get; }

    private void PersistOnChange()
    {
        AgentRoot.Skip(1).Subscribe(v => _config.AgentRoot = v).DisposeWith(_disposables);
        WorkspaceRoot.Skip(1).Subscribe(v => _config.WorkspaceRoot = v).DisposeWith(_disposables);
        SkillsDirectory.Skip(1).Subscribe(v => _config.SkillsDirectory = v).DisposeWith(_disposables);
        SubagentsDirectory.Skip(1).Subscribe(v => _config.SubagentsDirectory = v).DisposeWith(_disposables);
        McpConfigFileName.Skip(1).Subscribe(v => _config.McpConfigFileName = v).DisposeWith(_disposables);
        McpServersPropertyName.Skip(1).Subscribe(v => _config.McpServersPropertyName = v).DisposeWith(_disposables);
        InstallSkills.Skip(1).Subscribe(v => _config.InstallSkills = v).DisposeWith(_disposables);
        InstallSubagents.Skip(1).Subscribe(v => _config.InstallSubagents = v).DisposeWith(_disposables);
        InstallStdioMcp.Skip(1).Subscribe(v => _config.InstallStdioMcp = v).DisposeWith(_disposables);
        InstallLiveMcp.Skip(1).Subscribe(v => _config.InstallLiveMcp = v).DisposeWith(_disposables);
        SelectedLayout.Skip(1).Subscribe(v => _config.InstallLayout = v.Layout.ToString()).DisposeWith(_disposables);
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
            Uri? liveMcpUri = TryCreateLiveMcpUri();

            AgentToolkitInstallResult result = await AgentToolkitInstaller.InstallAsync(
                new AgentToolkitInstallOptions
                {
                    AgentRoot = AgentRoot.Value,
                    Layout = SelectedLayout.Value.Layout,
                    SkillsDirectory = SkillsDirectory.Value,
                    SubagentsDirectory = SubagentsDirectory.Value,
                    InstallSkills = InstallSkills.Value,
                    InstallSubagents = InstallSubagents.Value,
                    InstallStdioMcp = InstallStdioMcp.Value,
                    InstallLiveMcp = InstallLiveMcp.Value && liveMcpUri is not null,
                    McpConfigFileName = McpConfigFileName.Value,
                    McpServersPropertyName = McpServersPropertyName.Value,
                    WorkspaceRoot = WorkspaceRoot.Value,
                    StdioMcpCommand = McpCommand.Value,
                    StdioMcpArguments = ParseArguments(McpArguments.Value),
                    LiveMcpUri = InstallLiveMcp.Value ? liveMcpUri : null,
                },
                BundledAgentToolkitAssets.Load());

            foreach (string file in result.InstalledFiles)
            {
                InstalledFiles.Add(file);
            }

            HasInstalledFiles.Value = InstalledFiles.Count > 0;
            Status.Value = string.Format(SettingsStrings.AiAgents_InstallCompleted, result.InstalledFiles.Count);
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

    private void RefreshLiveEndpoint()
    {
        Uri? uri = TryCreateLiveMcpUri();
        LiveMcpUrl.Value = uri?.ToString() ?? "";
        IsLiveMcpAvailable.Value = uri is not null;
    }

    private void UpdatePresetDirectories(AgentInstallLayoutItem item)
    {
        SkillsDirectory.Value = item.SkillsDirectory;
        SubagentsDirectory.Value = item.SubagentsDirectory;
    }

    private Uri? TryCreateLiveMcpUri()
    {
        if (_agentHostEndpoint.EndpointUri is not { } endpoint)
        {
            return null;
        }

        var builder = new UriBuilder(endpoint);
        string query = builder.Query.TrimStart('?');
        string token = "token=" + Uri.EscapeDataString(_agentHostEndpoint.Token);
        builder.Query = string.IsNullOrWhiteSpace(query) ? token : query + "&" + token;
        return builder.Uri;
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

    private static string GetDefaultAgentRoot()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
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

public sealed record AgentInstallLayoutItem(
    string DisplayName,
    AgentToolkitInstallLayout Layout,
    string SkillsDirectory,
    string SubagentsDirectory);
