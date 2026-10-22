using System.Text.RegularExpressions;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.ProjectSystem;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.SceneSettingsTab;

// Changes one scene setting at a time from the palette, without opening the scene settings tab.
public sealed partial class SceneSettingsTabExtension
{
    public override IEnumerable<ContextCommandDefinition> ContextCommands =>
    [
        Command("sceneSettings.changeSceneSize", Strings.ChangeSceneSize, ChangeSizeAsync),
        Command("sceneSettings.changeSceneStart", Strings.ChangeSceneStart, ChangeStartAsync),
        Command("sceneSettings.changeSceneDuration", Strings.ChangeSceneDuration, ChangeDurationAsync),
    ];

    private static ContextCommandDefinition Command(
        string id, string displayName, Func<IEditorContext, Scene, IContextCommandInteraction, Task> execute)
        => new(id, displayName, "", [])
        {
            Scope = ContextCommandScope.Extension,
            CanExecute = execution => execution.EditorContext?.GetService<Scene>() is not null,
            ExecuteAsync = execution => execution is { Interaction: { } interaction, EditorContext: { } editorContext }
                && editorContext.GetService<Scene>() is { } scene
                    ? execute(editorContext, scene, interaction)
                    : Task.CompletedTask
        };

    private static async Task ChangeSizeAsync(IEditorContext editorContext, Scene scene, IContextCommandInteraction interaction)
    {
        string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Placeholder = "1920x1080",
            Value = $"{scene.FrameSize.Width}x{scene.FrameSize.Height}",
            Validate = value => TryParseSize(value, out _) ? null : Strings.CommandPalette_SceneSizeInvalid
        });
        if (input is not null && TryParseSize(input, out PixelSize size))
            await ApplyAsync(editorContext, scene, interaction.CancellationToken, frameSize: size);
    }

    private static async Task ChangeStartAsync(IEditorContext editorContext, Scene scene, IContextCommandInteraction interaction)
    {
        string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Placeholder = Strings.CommandPalette_TimeSpanHint,
            Value = TimeText.Format(scene.Start),
            Validate = SceneSettingsTabViewModel.StartValidator
        });
        if (input is not null && TimeText.TryParse(input, out MediaTime start))
            await ApplyAsync(editorContext, scene, interaction.CancellationToken, start: start);
    }

    private static async Task ChangeDurationAsync(IEditorContext editorContext, Scene scene, IContextCommandInteraction interaction)
    {
        string? input = await interaction.ShowInputAsync(new ContextCommandInputOptions
        {
            Placeholder = Strings.CommandPalette_TimeSpanHint,
            Value = TimeText.Format(scene.Duration),
            Validate = SceneSettingsTabViewModel.DurationValidator
        });
        if (input is not null && TimeText.TryParse(input, out MediaTime duration))
            await ApplyAsync(editorContext, scene, interaction.CancellationToken, duration: duration);
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
        IEditorContext editorContext, Scene scene, CancellationToken cancellationToken,
        PixelSize? frameSize = null, MediaTime? start = null, MediaTime? duration = null)
    {
        bool changes = frameSize is { } f && f != scene.FrameSize
            || start is { } s && s != scene.Start
            || duration is { } d && d != scene.Duration;
        if (!changes) return;

        // Applying rebuilds the renderer, which must not happen mid-playback. Pause also returns a pause
        // still draining the last frame, which IsPlaying no longer shows.
        if (editorContext.GetService<IPreviewPlayer>() is { } player)
        {
            await player.Pause();
        }

        // Switching editors while pausing cancels the command; the scene left behind stays as it was.
        if (cancellationToken.IsCancellationRequested) return;

        editorContext.GetRequiredService<ISceneSettingsService>().Apply(
            scene,
            frameSize ?? scene.FrameSize,
            start ?? scene.Start,
            duration ?? scene.Duration);
    }

    [GeneratedRegex(@"^\s*(\d+)\s*[xX×,]\s*(\d+)\s*$")]
    private static partial Regex SizeRegex();
}
