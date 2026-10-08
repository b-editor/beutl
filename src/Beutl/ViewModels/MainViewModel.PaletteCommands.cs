using Beutl.Configuration;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;
using Beutl.Services;

namespace Beutl.ViewModels;

// MainView commands that take their arguments from the command palette's input and pick steps.
public sealed partial class MainViewModel
{
    // Null when the command is not one of these, so the caller falls back to the menu bar's command.
    private Task? ExecutePaletteCommand(string commandName, IContextCommandInteraction interaction)
    {
        return commandName switch
        {
            "OpenRecentProject" => OpenRecentProjectAsync(interaction),
            "OpenRecentFile" => OpenRecentFileAsync(interaction),
            "ChangeTheme" => ChangeThemeAsync(interaction),
            "CommitVersion" => CommitVersionAsync(interaction),
            _ => null,
        };
    }

    // Null for the commands the menu bar answers; these exist only in the palette.
    private bool? CanExecutePaletteOnlyCommand(string commandName)
    {
        return commandName switch
        {
            "OpenRecentProject" => MenuBar.RecentProjectItems.Count > 0,
            "OpenRecentFile" => MenuBar.RecentFileItems.Count > 0,
            "ChangeTheme" => ThemeRegistry.Enumerate().Count > 1,
            _ => null,
        };
    }

    private async Task OpenRecentProjectAsync(IContextCommandInteraction interaction)
    {
        if (await PickRecentAsync(MenuBar.RecentProjectItems, interaction) is { } file)
        {
            await MenuBar.OpenRecentProject.ExecuteAsync(file);
        }
    }

    private async Task OpenRecentFileAsync(IContextCommandInteraction interaction)
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

    private static async Task ChangeThemeAsync(IContextCommandInteraction interaction)
    {
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        string? currentId = ThemeRegistry.ResolveOrDefault(config.Theme)?.Id;
        ContextCommandPickItem<ThemeDescriptor>[] items = ThemeRegistry.Enumerate()
            .Select(theme => new ContextCommandPickItem<ThemeDescriptor>(
                theme.DisplayName, theme, CommandPaletteInput.DescribeCurrent(theme.Id == currentId)))
            .ToArray();
        if (await interaction.ShowQuickPickAsync(items) is { } picked)
        {
            // ThemeService applies the theme when the setting changes.
            config.Theme = picked.Value.Id;
        }
    }

    private async Task CommitVersionAsync(IContextCommandInteraction interaction)
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
        if (!IsCurrentCommitTarget(
                expectedProject,
                expectedService,
                _projectService.CurrentProject.Value,
                _versionControlCoordinator.CurrentService))
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

    internal static bool IsCurrentCommitTarget(
        Project expectedProject,
        object expectedService,
        Project? currentProject,
        object? currentService)
    {
        return ReferenceEquals(expectedProject, currentProject)
               && ReferenceEquals(expectedService, currentService);
    }
}
