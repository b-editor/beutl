using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.ViewModels;

// MainView commands that take their arguments from the command palette's input and pick steps.
public sealed partial class MainViewModel
{
    internal async Task OpenRecentProjectAsync(IContextCommandInteraction interaction)
    {
        if (await PickRecentAsync(MenuBar.RecentProjectItems, interaction) is { } file)
        {
            await MenuBar.OpenRecentProject.ExecuteAsync(file);
        }
    }

    internal async Task OpenRecentFileAsync(IContextCommandInteraction interaction)
    {
        if (await PickRecentAsync(MenuBar.RecentFileItems, interaction) is { } file)
        {
            MenuBar.OpenRecentFile.Execute(file);
        }
    }

    // Newest first, as in the menu. The folder tells apart files that share a name.
    private static async Task<string?> PickRecentAsync(
        IEnumerable<string> files, IContextCommandInteraction interaction)
    {
        ContextCommandPickItem<string>[] items = files
            .Select(file => new ContextCommandPickItem<string>(
                Path.GetFileName(file), file, Path.GetDirectoryName(file)))
            .ToArray();
        return (await interaction.ShowQuickPickAsync(items))?.Value;
    }

    internal static async Task ChangeThemeAsync(IContextCommandInteraction interaction)
    {
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        string? currentId = ThemeRegistry.ResolveOrDefault(config.Theme.Value)?.Id;
        ContextCommandPickItem<ThemeDescriptor>[] items = ThemeRegistry.Enumerate()
            .Select(theme => new ContextCommandPickItem<ThemeDescriptor>(
                theme.DisplayName, theme, CommandPaletteInput.DescribeCurrent(theme.Id == currentId)))
            .ToArray();
        if (await interaction.ShowQuickPickAsync(items) is { } picked)
        {
            // ThemeService applies the theme when the setting changes.
            config.Theme.Value = picked.Value.Id;
        }
    }

    internal async Task CommitVersionAsync(IContextCommandInteraction interaction)
    {
        Project? expectedProject = _projectService.CurrentProject.Value;
        IProjectVersionControlService? expectedService = _versionControlCoordinator.CurrentService;
        if (expectedProject is null || expectedService is null) return;

        string? message = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Prompt = Strings.VersionControl_CommitMessage,
            Validate = CommandPaletteInput.Required
        });
        if (message is not null)
        {
            await CommitVersionAsync(expectedProject, expectedService, message);
        }
    }

    // Shared by the menu's flyout and the palette's input step.
    internal async Task CommitVersionAsync(
        Project expectedProject, IProjectVersionControlService expectedService, string message)
    {
        // The project or its repository may have been replaced while the message was being typed.
        if (!ReferenceEquals(expectedProject, _projectService.CurrentProject.Value)
            || !ReferenceEquals(expectedService, _versionControlCoordinator.CurrentService))
        {
            return;
        }

        try
        {
            CommitResult result = await _versionControlCoordinator.CommitManualAsync(message.Trim());
            NotificationService.ShowInformation(
                Strings.VersionControl,
                result is CommitResult.NoChanges
                    ? Strings.VersionControl_NothingToCommit
                    : Strings.VersionControl_CommitCreated);
        }
        catch (GitIdentityRequiredException)
        {
        }
        catch (Exception ex)
        {
            await ex.Handle();
        }
    }
}
