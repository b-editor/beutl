using System.Text.RegularExpressions;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.SceneSettingsTab;

// Changes one scene setting at a time from the palette, without opening the scene settings tab.
public sealed partial class SceneSettingsTabExtension : IContextCommandHandler
{
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        new("ChangeSceneSize", Strings.ChangeSceneSize, "", []) { Scope = ContextCommandScope.Extension },
        new("ChangeSceneStart", Strings.ChangeSceneStart, "", []) { Scope = ContextCommandScope.Extension },
        new("ChangeSceneDuration", Strings.ChangeSceneDuration, "", []) { Scope = ContextCommandScope.Extension },
    ];

    public bool CanExecute(ContextCommandExecution execution)
    {
        return execution.CommandName is "ChangeSceneSize" or "ChangeSceneStart" or "ChangeSceneDuration"
            && execution.EditorContext?.GetService<Scene>() is not null;
    }

    public async Task ExecuteAsync(ContextCommandExecution execution)
    {
        if (execution.Interaction is not { } interaction
            || execution.EditorContext is not { } editorContext
            || editorContext.GetService<Scene>() is not { } scene)
        {
            return;
        }

        switch (execution.CommandName)
        {
            case "ChangeSceneSize":
                {
                    string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
                    {
                        Placeholder = "1920x1080",
                        Value = $"{scene.FrameSize.Width}x{scene.FrameSize.Height}",
                        Validate = value => TryParseSize(value, out _) ? null : Strings.CommandPalette_SceneSizeInvalid
                    });
                    if (input is not null && TryParseSize(input, out PixelSize size))
                        await ApplyAsync(editorContext, scene, frameSize: size);
                    break;
                }

            case "ChangeSceneStart":
                {
                    string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
                    {
                        Placeholder = Strings.CommandPalette_TimeSpanHint,
                        Value = scene.Start.ToString(),
                        Validate = SceneSettingsTabViewModel.StartValidator
                    });
                    if (input is not null && TimeSpan.TryParse(input, out TimeSpan start))
                        await ApplyAsync(editorContext, scene, start: start);
                    break;
                }

            case "ChangeSceneDuration":
                {
                    string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
                    {
                        Placeholder = Strings.CommandPalette_TimeSpanHint,
                        Value = scene.Duration.ToString(),
                        Validate = SceneSettingsTabViewModel.DurationValidator
                    });
                    if (input is not null && TimeSpan.TryParse(input, out TimeSpan duration))
                        await ApplyAsync(editorContext, scene, duration: duration);
                    break;
                }
        }
    }

    // Accepts "1920x1080" as well as "1920 x 1080", "1920×1080" and "1920,1080".
    internal static bool TryParseSize(string value, out PixelSize size)
    {
        Match match = SizeRegex().Match(value);
        if (match.Success
            && int.TryParse(match.Groups[1].ValueSpan, out int width) && width > 0
            && int.TryParse(match.Groups[2].ValueSpan, out int height) && height > 0)
        {
            size = new PixelSize(width, height);
            return true;
        }

        size = default;
        return false;
    }

    // The settings left out keep the scene's values as of the change, not as of the prompt.
    private static async Task ApplyAsync(
        IEditorContext editorContext, Scene scene,
        PixelSize? frameSize = null, TimeSpan? start = null, TimeSpan? duration = null)
    {
        bool changes = frameSize is { } f && f != scene.FrameSize
            || start is { } s && s != scene.Start
            || duration is { } d && d != scene.Duration;
        if (!changes) return;

        // As in the tab: applying rebuilds the renderer, which must not happen mid-playback.
        if (editorContext.GetService<IPreviewPlayer>() is { IsPlaying.Value: true } player)
        {
            await player.Pause();
        }

        editorContext.GetRequiredService<ISceneSettingsService>().Apply(
            scene,
            frameSize ?? scene.FrameSize,
            start ?? scene.Start,
            duration ?? scene.Duration);
    }

    [GeneratedRegex(@"^\s*(\d+)\s*[xX×,]\s*(\d+)\s*$")]
    private static partial Regex SizeRegex();
}
