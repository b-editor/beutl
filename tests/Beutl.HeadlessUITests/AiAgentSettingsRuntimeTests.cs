using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.AgentHost;
using Beutl.Agents;
using Beutl.Configuration;
using Beutl.Controls;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiAgentSettingsRuntimeTests
{
    [AvaloniaTest]
    public async Task Runtime_defaults_bind_to_settings_and_restore_when_reopened()
    {
        var config = new AiAgentConfig
        {
            ClaudeCodeExecutablePath = { Value = Path.Combine(BeutlHomeIsolation.CurrentHome!, "missing-claude") },
            CodexExecutablePath = { Value = Environment.ProcessPath! }
        };
        using var viewModel = new AiAgentSettingsPageViewModel(config);
        var settings = viewModel.RuntimeSettings;
        settings.NewProviderId.Value = "editing-agent";
        settings.NewProviderName.Value = "Editing agent";
        settings.NewProviderCommand.Value = "acp-edit";
        settings.NewProviderArguments.Value = "serve\n--stdio";
        settings.AddProvider.Execute();
        await settings.PendingProbe;

        var page = new AiAgentSettingsPage { DataContext = viewModel };
        var window = new Window { Content = page, Width = 780, Height = 860, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var provider = page.FindControl<ComboBox>("RuntimeDefaultProvider")!;
            var permission = page.FindControl<ComboBox>("RuntimeDefaultPermission")!;
            var model = page.FindControl<TextBox>("RuntimeDefaultModel")!;
            var sendKey = page.FindControl<ComboBox>("RuntimeComposerSendKey")!;
            var sendAction = page.FindControl<ComboBox>("RuntimeSendAction")!;
            Assert.That(((AgentRuntimeProviderOption)provider.SelectedItem!).Id, Is.Empty);
            Assert.That(permission.ItemCount, Is.EqualTo(4));
            Assert.That(((AgentComposerSendKeyOption)sendKey.SelectedItem!).Key, Is.EqualTo(AgentComposerSendKey.Enter));
            Assert.That(sendKey.ItemCount, Is.EqualTo(2));
            Assert.That(((AgentRuntimeSendOption)sendAction.SelectedItem!).Steer, Is.False);
            provider.SelectedItem = settings.ProviderChoices.Value.Single(option => option.Id == "editing-agent");
            permission.SelectedItem = settings.PermissionChoices.Single(option => option.Mode == AgentPermissionMode.Supervised);
            model.Text = "example-model";
            sendKey.SelectedItem = settings.ComposerSendKeys.Single(option => option.Key == AgentComposerSendKey.CtrlEnter);
            sendAction.SelectedItem = settings.SendActions.Single(option => option.Steer);
            HeadlessTestHelpers.Render(3);

            Assert.Multiple(() =>
            {
                Assert.That(config.DefaultRuntimeProviderId.Value, Is.EqualTo("editing-agent"));
                Assert.That(config.DefaultPermissionMode.Value, Is.EqualTo(nameof(AgentPermissionMode.Supervised)));
                Assert.That(config.DefaultModel.Value, Is.EqualTo("example-model"));
                Assert.That(config.ComposerSendKey.Value, Is.EqualTo(AgentComposerSendKey.CtrlEnter));
                Assert.That(config.SteerOnSend.Value, Is.True);
                Assert.That(page.GetVisualDescendants().OfType<OptionsDisplayItem>()
                    .Select(row => row.Header), Does.Contain(SettingsStrings.AiAgents_Scope));
            });
            HeadlessCapture.SaveIfRequested(page, "ai-agent-settings", "runtime");
            var addProvider = page.FindControl<OptionsDisplayItem>("RuntimeAddProvider")!;
            addProvider.ContentTransition = null;
            addProvider.IsExpanded = true;
            HeadlessTestHelpers.Render(3);
            ((Control)addProvider.Content!).BringIntoView();
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(page, "ai-agent-settings", "custom-provider-form");

            var remove = page.GetVisualDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, SettingsStrings.AiAgents_RuntimeRemove));
            HeadlessTestHelpers.Click(remove);
            Assert.That(settings.CustomProviders.Value, Is.Empty);
            Assert.That(((AgentRuntimeProviderOption)provider.SelectedItem!).Id, Is.Empty);
        }
        finally
        {
            window.Close();
        }

        config.DefaultRuntimeProviderId.Value = "codex";
        using var restored = new AiAgentSettingsPageViewModel(config);
        Assert.Multiple(() =>
        {
            Assert.That(restored.RuntimeSettings.DefaultProvider.Value!.Id, Is.EqualTo("codex"));
            Assert.That(restored.RuntimeSettings.DefaultPermission.Value!.Mode, Is.EqualTo(AgentPermissionMode.Supervised));
            Assert.That(restored.RuntimeSettings.DefaultModel.Value, Is.EqualTo("example-model"));
            Assert.That(restored.RuntimeSettings.ComposerSendKey.Value!.Key, Is.EqualTo(AgentComposerSendKey.CtrlEnter));
            Assert.That(restored.RuntimeSettings.SendAction.Value!.Steer, Is.True);
        });
        config.ComposerSendKey.Value = AgentComposerSendKey.Enter;
        Assert.That(restored.RuntimeSettings.ComposerSendKey.Value!.Key, Is.EqualTo(AgentComposerSendKey.Enter));
    }

    [AvaloniaTest]
    public async Task Overrides_reprobe_on_edit_and_on_request()
    {
        var config = new AiAgentConfig();
        using var viewModel = new AiAgentSettingsPageViewModel(config);
        var settings = viewModel.RuntimeSettings;
        string path = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"new-agent-{Guid.NewGuid():N}.exe");
        settings.ClaudeCodeExecutablePath.Value = path;
        settings.CodexExecutablePath.Value = Environment.ProcessPath!;
        await settings.PendingProbe;
        Assert.Multiple(() =>
        {
            Assert.That(config.ClaudeCodeExecutablePath.Value, Is.EqualTo(path));
            Assert.That(config.CodexExecutablePath.Value, Is.EqualTo(Environment.ProcessPath));
            Assert.That(settings.ClaudeCodeStatus.Value, Is.EqualTo(SettingsStrings.AiAgents_RuntimeNotFound));
            Assert.That(settings.CodexStatus.Value, Does.Contain(Environment.ProcessPath));
        });

        File.WriteAllText(path, "");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        settings.RecheckProviders.Execute();
        await settings.PendingProbe;
        Assert.That(settings.ClaudeCodeStatus.Value, Is.EqualTo(string.Format(SettingsStrings.AiAgents_RuntimeAvailable, path)));
    }

    [AvaloniaTest]
    public async Task Custom_providers_validate_and_round_trip_arguments_and_existing_options()
    {
        const string original = """[{"id":"existing","displayName":"Existing","command":"acp","arguments":[],"environment":{"FLAG":"on"},"permissionModes":{"supervised":"interactive"}}]""";
        var config = new AiAgentConfig { CustomRuntimeProvidersJson = { Value = original } };
        using var viewModel = new AiAgentSettingsPageViewModel(config);
        var settings = viewModel.RuntimeSettings;
        settings.AddProvider.Execute();
        Assert.That(settings.NewProviderError.Value, Is.EqualTo(SettingsStrings.AiAgents_RuntimeRequiredFields));
        settings.NewProviderName.Value = " Local agent ";
        settings.NewProviderCommand.Value = Environment.ProcessPath!;
        settings.NewProviderArguments.Value = "serve\r\n\r\nfile with spaces\n--stdio";
        foreach (string duplicate in new[] { "codex", "claude", " existing " })
        {
            settings.NewProviderId.Value = duplicate;
            settings.AddProvider.Execute();
            Assert.That(settings.NewProviderError.Value, Is.EqualTo(SettingsStrings.AiAgents_RuntimeDuplicateId));
            Assert.That(config.CustomRuntimeProvidersJson.Value, Is.EqualTo(original));
        }

        settings.NewProviderId.Value = " local ";
        settings.AddProvider.Execute();
        await settings.PendingProbe;
        var saved = AgentRuntimeConfiguration.ReadCustomProviders(config.CustomRuntimeProvidersJson.Value);
        Assert.Multiple(() =>
        {
            Assert.That(settings.NewProviderError.Value, Is.Empty);
            Assert.That(saved[0]!.Environment!["FLAG"], Is.EqualTo("on"));
            Assert.That(saved[0]!.PermissionModes![AgentPermissionMode.Supervised], Is.EqualTo("interactive"));
            Assert.That(saved[1]!.Id, Is.EqualTo("local"));
            Assert.That(saved[1]!.DisplayName, Is.EqualTo("Local agent"));
            Assert.That(saved[1]!.Arguments, Is.EqualTo(new[] { "serve", "file with spaces", "--stdio" }));
            Assert.That(settings.CustomProviders.Value.Single(item => item.Id == "local").Status,
                Does.Contain(Environment.ProcessPath));
        });

        config.CustomRuntimeProvidersJson.Value = "{";
        settings.AddProvider.Execute();
        Assert.That(settings.CustomProvidersError.Value, Is.EqualTo(SettingsStrings.AiAgents_RuntimeInvalidProviders));
        Assert.That(config.CustomRuntimeProvidersJson.Value, Is.EqualTo("{"));
    }
}
