using System.Collections.ObjectModel;
using System.Reactive.Disposables;
using Beutl.AgentHost;
using Beutl.AgentToolkit.Installation;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class AiAgentSettingsPageViewModel : IDisposable
{
    private readonly AgentHostEndpoint _agentHostEndpoint;
    private readonly CompositeDisposable _disposables = [];

    public AiAgentSettingsPageViewModel(AgentHostEndpoint agentHostEndpoint)
    {
        _agentHostEndpoint = agentHostEndpoint;

        AgentToolkitMcpServerCommand command = AgentToolkitMcpServerLocator.ResolveDefault();

        AgentRoot = new ReactivePropertySlim<string>(GetDefaultAgentRoot()).DisposeWith(_disposables);
        WorkspaceRoot = new ReactivePropertySlim<string>(GetDefaultWorkspaceRoot()).DisposeWith(_disposables);
        SkillsDirectory = new ReactivePropertySlim<string>(Layouts[0].SkillsDirectory).DisposeWith(_disposables);
        SubagentsDirectory = new ReactivePropertySlim<string>(Layouts[0].SubagentsDirectory).DisposeWith(_disposables);
        McpConfigFileName = new ReactivePropertySlim<string>(".mcp.json").DisposeWith(_disposables);
        McpServersPropertyName = new ReactivePropertySlim<string>("servers").DisposeWith(_disposables);
        McpCommand = new ReactivePropertySlim<string>(command.Command).DisposeWith(_disposables);
        McpArguments = new ReactivePropertySlim<string>(string.Join(Environment.NewLine, command.Arguments))
            .DisposeWith(_disposables);
        InstallSkills = new ReactivePropertySlim<bool>(true).DisposeWith(_disposables);
        InstallSubagents = new ReactivePropertySlim<bool>(true).DisposeWith(_disposables);
        InstallStdioMcp = new ReactivePropertySlim<bool>(true).DisposeWith(_disposables);
        InstallLiveMcp = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        LiveMcpUrl = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        Status = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsInstalling = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        SelectedLayout = new ReactivePropertySlim<AgentInstallLayoutItem>(Layouts[0]).DisposeWith(_disposables);
        SelectedLayout.Subscribe(UpdatePresetDirectories).DisposeWith(_disposables);

        RefreshLiveEndpoint();
        Install = new AsyncReactiveCommand()
            .WithSubscribe(InstallAsync)
            .DisposeWith(_disposables);
        RefreshLiveMcp = new ReactiveCommand()
            .WithSubscribe(RefreshLiveEndpoint)
            .DisposeWith(_disposables);
    }

    public IReadOnlyList<AgentInstallLayoutItem> Layouts { get; } =
    [
        new("Generic skills/agents folders", AgentToolkitInstallLayout.Generic, "skills", "agents"),
        new(
            "Claude Code .claude folders",
            AgentToolkitInstallLayout.ClaudeCode,
            Path.Combine(".claude", "skills"),
            Path.Combine(".claude", "agents")),
    ];

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

    public ReactivePropertySlim<string> Status { get; }

    public ReactivePropertySlim<bool> IsInstalling { get; }

    public ObservableCollection<string> InstalledFiles { get; } = [];

    public AsyncReactiveCommand Install { get; }

    public ReactiveCommand RefreshLiveMcp { get; }

    private async Task InstallAsync()
    {
        try
        {
            IsInstalling.Value = true;
            Status.Value = "";
            InstalledFiles.Clear();
            RefreshLiveEndpoint();

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
                    InstallLiveMcp = InstallLiveMcp.Value,
                    McpConfigFileName = McpConfigFileName.Value,
                    McpServersPropertyName = McpServersPropertyName.Value,
                    WorkspaceRoot = WorkspaceRoot.Value,
                    StdioMcpCommand = McpCommand.Value,
                    StdioMcpArguments = ParseArguments(McpArguments.Value),
                    LiveMcpUri = InstallLiveMcp.Value ? TryCreateLiveMcpUri() : null,
                },
                BundledAgentToolkitAssets.Load());

            foreach (string file in result.InstalledFiles)
            {
                InstalledFiles.Add(file);
            }

            Status.Value = $"Installed {result.InstalledFiles.Count} file(s).";
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
        if (uri is null)
        {
            InstallLiveMcp.Value = false;
        }
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
