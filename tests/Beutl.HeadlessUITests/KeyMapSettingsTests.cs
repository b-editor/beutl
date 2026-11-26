using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Editor.Components.GraphEditorTab;
using Beutl.Editor.Components.PathEditorTab;
using Beutl.Language;
using Beutl.Pages.SettingsPages;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels.SettingsPages;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class KeyMapSettingsTests
{
    [AvaloniaTest]
    public void The_extensions_window_refresh_shortcuts_appear_under_its_name()
    {
        var manager = TestShell.MainViewModel.ContextCommandManager;
        using var model = CreateModel(manager);
        var page = new KeyMapSettingsPage { DataContext = model };
        using var host = Show(page);
        page.FindControl<TextBox>("ShortcutSearchBox")!.Text = "extensions.refresh";
        HeadlessTestHelpers.Render();

        KeyMapSettingsGroup group = model.VisibleGroups.Value.Single();
        Assert.That(group.Extension, Is.SameAs(ExtensionsToolWindowExtension.Instance));
        Assert.That(page.GetVisualDescendants().OfType<Beutl.Controls.OptionsDisplayItem>()
            .Any(item => item.IsEffectivelyVisible && Equals(item.Header, group.Extension.DisplayName)), Is.True);
        Assert.That(page.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.Name == "AssignShortcutButton")
            .Select(button => ((KeyMapSettingsItem)button.DataContext!).KeyGesture.Value),
            Is.EquivalentTo(new[] { KeyGesture.Parse("F5"), KeyGesture.Parse("Ctrl+R"), KeyGesture.Parse("Cmd+R") }));
        HeadlessCapture.SaveIfRequested(page, "keymap", "extensions-window");
    }

    [AvaloniaTest]
    public void Searching_by_command_or_gesture_shows_matching_rows()
    {
        var manager = CreateKeymap();
        using var model = CreateModel(manager);
        var page = new KeyMapSettingsPage { DataContext = model };
        using var host = Show(page);
        TextBox search = page.FindControl<TextBox>("ShortcutSearchBox")!;

        search.Text = "Shift+B";
        HeadlessTestHelpers.Settle();
        KeyMapSettingsItem binding = model.VisibleGroups.Value.SelectMany(group => group.Items)
            .Single(item => item.Command.Definition.Id == "pathEditor.bend");
        Assert.That(binding.Command.Definition.Id, Is.EqualTo("pathEditor.bend"));
        Assert.That(binding.KeyGesture.Value, Is.EqualTo(KeyGesture.Parse("Shift+B")));

        search.Text = "no-shortcut-matches-this";
        HeadlessTestHelpers.Settle();
        Assert.That(model.HasResults.Value, Is.False);
        Assert.That(page.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.IsEffectivelyVisible && block.Text == SettingsStrings.Keymap_NoResults), Is.True);

        search.Text = Strings.PathEditor_BendTool;
        HeadlessTestHelpers.Settle();
        Assert.That(model.VisibleGroups.Value.Single().Items.Select(item => item.Command.Definition.Id),
            Is.EqualTo(new[] { "pathEditor.bend", "pathEditor.bend" }));
        Assert.That(model.VisibleGroups.Value.Single().IsExpanded.Value, Is.True);
        HeadlessCapture.SaveIfRequested(page, "keymap", "search");
    }

    [AvaloniaTest]
    public void A_conflict_in_the_same_control_is_shown_and_clears_after_reset()
    {
        var manager = CreateKeymap();
        using var model = CreateModel(manager);
        var page = new KeyMapSettingsPage { DataContext = model };
        using var host = Show(page, height: 540);
        page.FindControl<TextBox>("ShortcutSearchBox")!.Text = "timeline.toggle";
        var ripple = manager.GetDefinitions<TimelineTabExtension>().Single(entry => entry.Definition.Id == "timeline.toggleRippleMode");
        var platform = ContextCommandManager.CurrentPlatform;

        manager.ChangeKeyGesture(ripple, KeyGesture.Parse("S"), platform);
        HeadlessTestHelpers.Settle();
        string notice = string.Format(SettingsStrings.Keymap_Conflict, Strings.SlipTool);
        Assert.That(page.GetVisualDescendants().OfType<TextBlock>()
            .Any(block => block.IsEffectivelyVisible && block.Text == notice), Is.True);
        HeadlessCapture.SaveIfRequested(page, "keymap", "conflict");

        Button reset = page.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible
            && button.Name == "ResetShortcutButton" && button.DataContext is KeyMapSettingsItem item && item.Command == ripple);
        reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(ContextCommandManager.CurrentGestures(ripple).Single(), Is.EqualTo(KeyGesture.Parse("B")));
            Assert.That(manager.HasCustomKeyGestures(ripple, platform), Is.False);
            Assert.That(model.VisibleGroups.Value.SelectMany(group => group.Items).Select(item => item.ConflictMessage), Is.All.Null);
        });
    }

    [AvaloniaTest]
    public void Different_extensions_controls_and_mutually_exclusive_modes_can_share_keys()
    {
        var manager = CreateKeymap();
        var rename = manager.GetDefinitions<TimelineTabExtension>().Single(entry => entry.Definition.Id == "timeline.rename");
        manager.ChangeKeyGesture(rename, KeyGesture.Parse("B"), ContextCommandManager.CurrentPlatform);
        using var model = CreateModel(manager);
        string[] nonConflictingIds =
        [
            "timeline.rename", "timeline.toggleRippleMode", "pathEditor.bend",
            "timeline.exitRazorMode", "timeline.exitSlipMode", "timeline.exitSlideMode", "timeline.exitRollMode",
            "editor.shuttleBackwardFine", "graphEditor.previousKeyFrame"
        ];

        Assert.That(model.VisibleGroups.Value.SelectMany(group => group.Items)
            .Where(item => nonConflictingIds.Contains(item.Command.Definition.Id))
            .Select(item => item.ConflictMessage), Is.All.Null);
    }

    [Test]
    public void Reset_removes_old_and_new_overrides_for_one_platform_and_restores_all_its_defaults()
    {
        var json = JsonNode.Parse("""
            {
              "Windows": { "timeline.delete": null },
              "WINDOWS": { "timeline.delete": ["F7", null, "F8"], "timeline.copy": "F6" },
              "OSX": { "timeline.delete": ["F9", "Back"] }
            }
            """)!.AsObject();
        var settings = new ContextCommandSettingsStore(json, persist: false);
        var manager = new ContextCommandManager(settings);
        manager.Register(TimelineTabExtension.Instance);
        var entry = manager.GetDefinitions<TimelineTabExtension>().Single(command => command.Definition.Id == "timeline.delete");

        manager.ResetKeyGestures(entry, OSPlatform.Windows);

        Assert.Multiple(() =>
        {
            Assert.That(entry.KeyGestures.Where(gesture => gesture.Platform == OSPlatform.Windows).Select(gesture => gesture.KeyGesture),
                Is.EqualTo(new[] { KeyGesture.Parse("Ctrl+Delete"), KeyGesture.Parse("Delete") }));
            Assert.That(settings.Restore("timeline.delete").Select(setting => setting.Platform), Is.EqualTo(new[] { OSPlatform.OSX }));
            Assert.That(settings.Restore("timeline.copy").Single().Gestures, Is.EqualTo(new[] { KeyGesture.Parse("F6") }));
            Assert.That(settings.Clear("timeline.delete", OSPlatform.Windows), Is.False);
        });
    }

    private static ContextCommandManager CreateKeymap()
    {
        var manager = new ContextCommandManager(new ContextCommandSettingsStore([], persist: false));
        manager.Register(TimelineTabExtension.Instance);
        manager.Register(PathEditorTabExtension.Instance);
        manager.Register(GraphEditorTabExtension.Instance);
        manager.Register(SceneEditorExtension.Instance);
        return manager;
    }

    private static KeyMapSettingsPageViewModel CreateModel(ContextCommandManager manager)
        => new(manager, TestShell.MainViewModel.ExtensionProvider);

    private static ControlHost Show(KeyMapSettingsPage page, double height = 470)
        => new(new Window { Content = page, Width = 790, Height = height, RequestedThemeVariant = ThemeVariant.Dark });
}
