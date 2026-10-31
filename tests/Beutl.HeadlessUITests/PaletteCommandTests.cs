using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.DockLayoutTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.HistoryTab;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Editor.Components.PreviewSettingsTab;
using Beutl.Editor.Components.SceneSettingsTab;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

// The commands that ask for their arguments in the command palette, driven with scripted answers.
[TestFixture]
[NonParallelizable]
public class PaletteCommandTests
{
    private static async Task<EditViewModel> OpenEditorForNewScene(string name, int frameRate = 30)
        => (await TestShell.OpenNewSceneAsync(name, frameRate: frameRate)).Editor;

    private static ContextCommandExecution Execution(
        string commandName, IEditorContext editor, ScriptedInteraction? interaction = null)
    {
        return new ContextCommandExecution(commandName) { EditorContext = editor, Interaction = interaction };
    }

    // Runs a command the way the palette runs one that its extension handles itself.
    private static Task RunAsync(ViewExtension extension, ContextCommandExecution execution)
        => extension.RunAsync(null, execution);

    private static bool CanRun(ViewExtension extension, ContextCommandExecution execution)
        => extension.CanRun(null, execution);

    // Runs a scene editor command on the editor, as a key or the palette does.
    private static Task RunAsync(EditViewModel editor, ContextCommandExecution execution)
        => SceneEditorExtension.Instance.RunAsync(editor, execution);

    private static bool CanRun(EditViewModel editor, ContextCommandExecution execution)
        => SceneEditorExtension.Instance.CanRun(editor, execution);

    private static void SetPlayhead(EditViewModel editor, double seconds)
    {
        editor.GetService<IEditorClock>()!.Seek(MediaTime.FromSeconds(seconds));
    }

    private static MediaTime Playhead(EditViewModel editor) => editor.GetService<IEditorClock>()!.CurrentTime.Value;

    private static async Task<Element> AddRectAsync(EditViewModel editor, double start, double length, int layer)
    {
        ElementAddResult result = await editor.GetService<IElementAdder>()!.AddAsync(
            [
                new ElementDescription(
                    MediaTime.FromSeconds(start), MediaTime.FromSeconds(length), layer,
                    new ElementSource.EngineObject(() => new RectShape()))
            ],
            CancellationToken.None);
        HeadlessTestHelpers.Settle();
        return result.Items.Single().PrimaryElement;
    }

    private static void CloseTimeline(EditViewModel editor)
    {
        editor.CloseToolTab(editor.FindToolTab<TimelineTabViewModel>()!);
        HeadlessTestHelpers.Settle();
        Assert.That(editor.FindToolTab<TimelineTabViewModel>(), Is.Null);
    }

    private static string LastHistoryName(EditViewModel editor) => editor.HistoryManager.GetEntriesSnapshot()[^1].DisplayLabel;

    private static CommandPaletteItemViewModel PaletteSplit(CommandPaletteViewModel palette)
    {
        return palette.FilteredCommands.Single(i => i.Command.Id == "timeline.split");
    }

    [AvaloniaTest]
    public async Task Goto_timecode_asks_for_the_timecode_in_the_palette()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-goto-timecode");
        var interaction = new ScriptedInteraction().Input(options =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(options.Value, Is.EqualTo("00:00:00.000"));
                Assert.That(options.Validate!("nonsense"), Is.EqualTo(Strings.GotoTimecode_InvalidFormat));
                Assert.That(options.Validate!("@missing"), Is.EqualTo(Strings.GotoTimecode_MarkerNotFound));
            });
            return "60f";
        });

        await RunAsync(editor, Execution("editor.gotoTimecode", editor, interaction));

        Assert.That(Playhead(editor), Is.EqualTo(MediaTime.FromSeconds(2)));
        Assert.That(interaction.IsDone, Is.True);
    }

    [TestCase(0, 30, "00:00:00.000")]
    [TestCase(1, 120, "00:00:00.008")]
    [TestCase(119, 120, "00:00:00.991")]
    [TestCase(30 * 3600 * 23 + 15, 30, "23:00:00.500")]
    [TestCase(30 * 3600 * 25, 30, "1.01:00:00.000")]
    public void Palette_timecodes_parse_back_to_the_same_frame(long frame, int rate, string expected)
    {
        MediaTime time = MediaTime.FromSeconds(frame / (double)rate).RoundToFrame(new Rational(rate), MidpointRounding.AwayFromZero);
        string text = CommandPaletteInput.FormatTimecode(time);

        bool parsed = GotoTimecodeParser.TryParse(text, new Rational(rate), MediaTime.Zero, [], out MediaTime result, out _);

        Assert.That((text, parsed, result.RoundToFrame(new Rational(rate), MidpointRounding.AwayFromZero)), Is.EqualTo((expected, true, time)));
    }

    [AvaloniaTest]
    public async Task Accepting_the_prefilled_timecode_keeps_a_frame_milliseconds_cannot_name()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-goto-999fps", frameRate: 999);
        // 501 / 999 s prints as 00:00:00.501, which snaps back to frame 500.
        MediaTime frame501 = MediaTime.FromSeconds(501 / 999d).RoundToFrame(new Rational(999), MidpointRounding.AwayFromZero);
        editor.GetService<IEditorClock>()!.Seek(frame501);
        HeadlessTestHelpers.Settle();
        string? prefilled = null;

        await RunAsync(editor, Execution("editor.gotoTimecode", editor,
            new ScriptedInteraction().Input(options => prefilled = options.Value)));

        Assert.That((prefilled, Playhead(editor)), Is.EqualTo(("00:00:00.501", frame501)));
    }

    [AvaloniaTest]
    public async Task Goto_timecode_from_a_key_still_opens_the_inline_editor()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-goto-timecode-key");
        int requests = 0;
        using IDisposable subscription = editor.Player.BeginEditTimecodeRequested.Subscribe(_ => requests++);

        await RunAsync(editor, new ContextCommandExecution("editor.gotoTimecode"));

        Assert.That(requests, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task Marker_commands_add_rename_and_seek_without_the_timeline()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-markers");
        Scene scene = editor.Scene;
        Assert.That(CanRun(editor, Execution("editor.goToMarker", editor)), Is.False, "No marker to go to yet.");

        SetPlayhead(editor, 1);
        await RunAsync(editor, Execution("editor.addMarker", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.That(options.Validate!("  "), Is.EqualTo(Strings.CommandPalette_ValueRequired));
            return options.Value == "Marker 1" ? "Opening" : null;
        })));
        Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.AddMarker));

        // The same frame keeps one marker, renamed instead of duplicated.
        await RunAsync(editor, Execution("editor.addMarker", editor,
            new ScriptedInteraction().Input(options => options.Value == "Opening" ? "Intro" : null)));
        SetPlayhead(editor, 3);
        await RunAsync(editor, Execution("editor.addMarker", editor, new ScriptedInteraction().Input("Outro")));
        Assert.That(scene.Markers.Select(m => (m.Time.Value, m.Name.Value)), Is.EqualTo(new[]
        {
            (MediaTime.FromSeconds(1), "Intro"),
            (MediaTime.FromSeconds(3), "Outro"),
        }));

        SetPlayhead(editor, 0);
        var goTo = new ScriptedInteraction().Pick<SceneMarker>(item => item.Label == "Outro");
        await RunAsync(editor, Execution("editor.goToMarker", editor, goTo));
        Assert.Multiple(() =>
        {
            Assert.That(Playhead(editor), Is.EqualTo(MediaTime.FromSeconds(3)));
            Assert.That(goTo.PickedFrom<SceneMarker>().Select(i => (i.Label, i.Description)), Is.EqualTo(new[]
            {
                ("Intro", "00:00:01.000"),
                ("Outro", "00:00:03.000"),
            }));
        });

        await RunAsync(editor, Execution("editor.renameMarker", editor, new ScriptedInteraction()
            .Pick<SceneMarker>(item => item.Label == "Intro")
            .Input(options => options.Value == "Intro" ? "Cold open" : null)));
        Assert.Multiple(() =>
        {
            Assert.That(scene.Markers[0].Name.Value, Is.EqualTo("Cold open"));
            Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.EditMarker));
        });

        await editor.UndoAsync();
        Assert.That(scene.Markers[0].Name.Value, Is.EqualTo("Intro"));
    }

    [AvaloniaTest]
    public async Task Rename_split_and_save_as_template_act_on_the_selected_element_with_the_timeline_closed()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-element");
        TimelineTabExtension timeline = TimelineTabExtension.Instance;
        Element element = await AddRectAsync(editor, 0, 4, 0);
        CloseTimeline(editor);
        Assert.That(CanRun(timeline, Execution("timeline.rename", editor)), Is.False, "Nothing is selected yet.");

        editor.GetService<IEditorSelection>()!.Select(element);
        await RunAsync(timeline, Execution("timeline.rename", editor, new ScriptedInteraction()
            .Input(options => options.Value == element.Name.Value ? "Title" : null)));
        Assert.Multiple(() =>
        {
            Assert.That(element.Name.Value, Is.EqualTo("Title"));
            Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.RenameElement));
        });

        SetPlayhead(editor, 4);
        Assert.That(CanRun(timeline, Execution("timeline.split", editor)), Is.False, "The playhead is past the element.");
        SetPlayhead(editor, 1);
        await RunAsync(timeline, Execution("timeline.split", editor));
        Assert.That(editor.Scene.Children.Select(e => (e.Start.Value, e.Length.Value)), Is.EquivalentTo(new[]
        {
            (MediaTime.Zero, MediaTime.FromSeconds(1)),
            (MediaTime.FromSeconds(1), MediaTime.FromSeconds(3)),
        }));

        // Saving a template is refused for a name that cannot be a file name; cancelling saves nothing.
        int templates = ObjectTemplateService.Instance.FindByBaseType(typeof(Element)).Count();
        await RunAsync(timeline, Execution("timeline.saveAsTemplate", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.That(options.Validate!("a/b"), Is.EqualTo(MessageStrings.InvalidString));
            return null;
        })));
        Assert.That(ObjectTemplateService.Instance.FindByBaseType(typeof(Element)).Count(), Is.EqualTo(templates));

        element.IsLocked.Value = true;
        Assert.Multiple(() =>
        {
            Assert.That(CanRun(timeline, Execution("timeline.rename", editor)), Is.False);
            Assert.That(CanRun(timeline, Execution("timeline.split", editor)), Is.False);
            Assert.That(CanRun(timeline, Execution("timeline.saveAsTemplate", editor)), Is.True);
        });
    }

    [AvaloniaTest]
    public async Task Save_as_template_saves_the_selected_clips_with_their_groups_as_one_template()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-save-set-template");
        Element first = await AddRectAsync(editor, 2, 2, 1);
        Element second = await AddRectAsync(editor, 3, 2, 2);
        Element grouped = await AddRectAsync(editor, 6, 2, 3);
        await AddRectAsync(editor, 0, 1, 0);
        editor.Scene.Groups.Add([second.Id.Value, grouped.Id.Value]);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.SelectElement(timeline.GetViewModelFor(first)!);
        timeline.SelectElement(timeline.GetViewModelFor(second)!);
        editor.GetService<IEditorSelection>()!.Select(first);
        string name = $"set-{Guid.NewGuid():N}";

        await RunAsync(TimelineTabExtension.Instance, Execution(
            "timeline.saveAsTemplate", editor, new ScriptedInteraction().Input(_ => name)));

        // The selection takes the clip grouped with one of its clips along, as copying does, and nothing else.
        ObjectTemplateItem saved = ObjectTemplateService.Instance.FindByBaseType(typeof(ElementSet))
            .Single(item => item.Name.Value == name);
        try
        {
            var set = (ElementSet)saved.CreateInstance()!;
            Assert.Multiple(() =>
            {
                Assert.That(
                    set.Elements.Select(e => (e.Start.Value, e.ZIndex.Value)),
                    Is.EquivalentTo(new[]
                    {
                        (MediaTime.Zero, 0),
                        (MediaTime.FromSeconds(1), 1),
                        (MediaTime.FromSeconds(4), 2),
                    }));
                Assert.That(set.Groups, Has.Count.EqualTo(1));
                Assert.That(set.Groups[0], Has.Count.EqualTo(2));
            });
        }
        finally
        {
            File.Delete(saved.FilePath!);
            ObjectTemplateService.Instance.RefreshFromFileSystem();
        }
    }

    [AvaloniaTest]
    public async Task Save_as_template_with_the_timeline_closed_saves_every_selected_clip()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-save-set-template-closed");
        Element first = await AddRectAsync(editor, 0, 2, 0);
        Element second = await AddRectAsync(editor, 3, 2, 1);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.SelectElement(timeline.GetViewModelFor(first)!);
        timeline.SelectElement(timeline.GetViewModelFor(second)!);
        editor.GetService<IEditorSelection>()!.Select(first);
        CloseTimeline(editor);
        string name = $"set-closed-{Guid.NewGuid():N}";

        await RunAsync(TimelineTabExtension.Instance, Execution(
            "timeline.saveAsTemplate", editor, new ScriptedInteraction().Input(_ => name)));

        // The selection outlives the timeline, so both clips are saved, not only the selected element.
        ObjectTemplateItem saved = ObjectTemplateService.Instance.FindByBaseType(typeof(ElementSet))
            .Single(item => item.Name.Value == name);
        try
        {
            var set = (ElementSet)saved.CreateInstance()!;
            Assert.That(
                set.Elements.Select(e => (e.Start.Value, e.ZIndex.Value)),
                Is.EquivalentTo(new[] { (MediaTime.Zero, 0), (MediaTime.FromSeconds(3), 1) }));
        }
        finally
        {
            File.Delete(saved.FilePath!);
            ObjectTemplateService.Instance.RefreshFromFileSystem();
        }
    }

    [AvaloniaTest]
    public async Task Inline_rename_writes_the_name_once_editing_ends_as_one_history_entry()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-inline-rename");
        Element element = await AddRectAsync(editor, 0, 2, 0);
        var elsewhere = new Button();
        var window = new Window
        {
            Content = new DockPanel { Children = { elsewhere, new EditView { DataContext = editor } } },
            Width = 1200,
            Height = 800
        };
        window.Show();
        try
        {
            HeadlessTestHelpers.Render(5);
            ElementView view = window.GetVisualDescendants().OfType<ElementView>().First();
            var textBox = (TextBox)view.GetVisualDescendants().OfType<Control>().First(c => c.Name == "textBox");
            int entries = editor.HistoryManager.GetEntriesSnapshot().Length;

            // The clip's own Rename gesture, which runs the command on its ElementViewModel.
            view.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = OperatingSystem.IsMacOS() ? Key.Enter : Key.F2
            });
            HeadlessTestHelpers.Render(5);
            Assert.That(textBox.IsFocused, Is.True, "Rename did not focus the editor, so typing would go elsewhere.");
            textBox.SelectAll();
            window.KeyTextInput("Ti");
            window.KeyTextInput("tle");
            Assert.That(element.Name.Value, Is.Not.EqualTo("Title"), "Each keystroke must not write the name.");

            elsewhere.Focus();
            HeadlessTestHelpers.Render(2);

            Assert.Multiple(() =>
            {
                Assert.That(element.Name.Value, Is.EqualTo("Title"));
                Assert.That(editor.HistoryManager.GetEntriesSnapshot(), Has.Length.EqualTo(entries + 1));
                Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.RenameElement));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    [AvaloniaTest]
    public async Task Split_with_the_timeline_open_splits_the_selection_like_the_shortcut()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-split-open");
        Element first = await AddRectAsync(editor, 0, 4, 0);
        Element second = await AddRectAsync(editor, 0, 4, 1);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.SelectElement(timeline.GetViewModelFor(first)!);
        timeline.SelectElement(timeline.GetViewModelFor(second)!);
        editor.GetService<IEditorSelection>()!.Select(second);
        SetPlayhead(editor, 2);

        await RunAsync(TimelineTabExtension.Instance, Execution("timeline.split", editor));
        HeadlessTestHelpers.Settle();

        Assert.That(editor.Scene.Children, Has.Count.EqualTo(4));
    }

    [AvaloniaTest]
    public async Task Split_is_unavailable_when_a_half_would_be_shorter_than_a_frame()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-split-subframe");
        Element element = await AddRectAsync(editor, 0, 4, 0);
        CloseTimeline(editor);
        editor.GetService<IEditorSelection>()!.Select(element);
        // Off the 30 fps grid, so the playhead's frame 31 lands inside the clip but under a frame from its start.
        element.Start.Value = MediaTime.FromSeconds(1.01);
        SetPlayhead(editor, 31 / 30d);

        Assert.That(CanRun(TimelineTabExtension.Instance, Execution("timeline.split", editor)), Is.False);

        SetPlayhead(editor, 2);
        Assert.That(CanRun(TimelineTabExtension.Instance, Execution("timeline.split", editor)), Is.True);
    }

    [AvaloniaTest]
    public async Task Split_is_available_when_another_selected_clip_crosses_the_playhead()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-split-staggered");
        Element first = await AddRectAsync(editor, 0, 4, 0);
        Element second = await AddRectAsync(editor, 5, 4, 1);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.SelectElement(timeline.GetViewModelFor(first)!);
        timeline.SelectElement(timeline.GetViewModelFor(second)!);
        // The selected clip ends before the playhead; the other selected clip crosses it.
        editor.GetService<IEditorSelection>()!.Select(first);
        SetPlayhead(editor, 6);

        Assert.That(CanRun(TimelineTabExtension.Instance, Execution("timeline.split", editor)), Is.True);
        await RunAsync(TimelineTabExtension.Instance, Execution("timeline.split", editor));
        HeadlessTestHelpers.Settle();

        Assert.That(editor.Scene.Children.Where(e => e.ZIndex.Value == 1).Select(e => (e.Start.Value, e.Length.Value)),
            Is.EquivalentTo(new[]
            {
                (MediaTime.FromSeconds(5), MediaTime.FromSeconds(1)),
                (MediaTime.FromSeconds(6), MediaTime.FromSeconds(3)),
            }));
    }

    [AvaloniaTest]
    public async Task The_open_palette_follows_the_playhead_and_the_selection_for_split()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-split-follows-playhead");
        Element element = await AddRectAsync(editor, 2, 4, 0);
        IEditorSelection selection = editor.GetService<IEditorSelection>()!;
        selection.Select(element);
        SetPlayhead(editor, 0);
        CommandPaletteViewModel palette = TestShell.MainViewModel.CommandPalette;
        try
        {
            palette.Open();
            Assert.That(PaletteSplit(palette).IsEnabled, Is.False, "The playhead is before the clip.");

            SetPlayhead(editor, 3);
            await HeadlessTestHelpers.WaitUntilAsync(() => PaletteSplit(palette).IsEnabled, message: "Split did not follow the playhead into the clip.");

            selection.Select(null);
            await HeadlessTestHelpers.WaitUntilAsync(() => !PaletteSplit(palette).IsEnabled, message: "Split did not follow the cleared selection.");
            Assert.That(palette.Query.Value, Is.Empty);
        }
        finally
        {
            palette.Close();
        }
    }

    // The open timeline's own commands are in the palette, so its selection changes refresh every entry,
    // Split included.
    [AvaloniaTest]
    public async Task The_open_palette_follows_the_timeline_multi_selection_for_split()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-split-timeline-selection");
        Element first = await AddRectAsync(editor, 0, 4, 0);
        Element second = await AddRectAsync(editor, 5, 4, 1);
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.SelectElement(timeline.GetViewModelFor(first)!);
        editor.GetService<IEditorSelection>()!.Select(first);
        SetPlayhead(editor, 6);
        CommandPaletteViewModel palette = TestShell.MainViewModel.CommandPalette;
        try
        {
            palette.Open();
            // The notifiers replay their current values on subscription; let that refresh pass first.
            await Task.Delay(200);
            HeadlessTestHelpers.Settle();
            Assert.That(PaletteSplit(palette).IsEnabled, Is.False, "The selected clip ends before the playhead.");

            timeline.SwitchSelectedElement(timeline.GetViewModelFor(second)!);
            await HeadlessTestHelpers.WaitUntilAsync(() => PaletteSplit(palette).IsEnabled, message: "Split did not follow the added clip.");
        }
        finally
        {
            palette.Close();
        }
    }

    [AvaloniaTest]
    public async Task Add_element_puts_the_picked_library_item_at_the_playhead_above_the_visible_clips()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-add-element");
        await AddRectAsync(editor, 0, 4, 0);
        CloseTimeline(editor);
        SetPlayhead(editor, 1);

        var interaction = new ScriptedInteraction().Pick<Type>(item => item.Value == typeof(EllipseShape));
        await RunAsync(LibraryTabExtension.Instance, Execution("library.addElement", editor, interaction));
        HeadlessTestHelpers.Settle();

        Element added = editor.Scene.Children.Single(e => e.Objects.OfType<EllipseShape>().Any());
        Assert.Multiple(() =>
        {
            Assert.That(added.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(added.ZIndex.Value, Is.EqualTo(1));
            // The library group goes in the description, where the pick filter also searches.
            Assert.That(interaction.PickedFrom<Type>().Single(i => i.Value == typeof(AudioWaveformDrawable)).Description,
                Is.EqualTo(GraphicsStrings.AudioVisualizer));
        });
    }

    [AvaloniaTest]
    public async Task Scene_settings_commands_change_one_setting_each()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-scene-settings");
        Scene scene = editor.Scene;
        SceneSettingsTabExtension settings = SceneSettingsTabExtension.Instance;
        Assert.That(settings.DisplayName, Is.EqualTo(Strings.SceneSettings));

        await RunAsync(settings, Execution("sceneSettings.changeSceneSize", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(options.Value, Is.EqualTo("640x480"));
                Assert.That(options.Validate!("0x480"), Is.EqualTo(Strings.CommandPalette_SceneSizeInvalid));
                Assert.That(options.Validate!("wide"), Is.EqualTo(Strings.CommandPalette_SceneSizeInvalid));
            });
            return "1280 × 720";
        })));
        await RunAsync(settings, Execution("sceneSettings.changeSceneDuration", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.That(options.Validate!("00:00:00"), Is.EqualTo(MessageStrings.ValueLessThanOrEqualToZero));
            return "00:00:07";
        })));
        await RunAsync(settings, Execution("sceneSettings.changeSceneStart", editor, new ScriptedInteraction().Input("00:00:01")));

        Assert.Multiple(() =>
        {
            Assert.That(scene.FrameSize.Value, Is.EqualTo(new PixelSize(1280, 720)));
            Assert.That(scene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(7)));
            Assert.That(scene.Start.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
            Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.ChangeSceneSettings));
        });
    }

    // A scene time confirmed as the palette shows it stays exact, and a typed frame time lands on the frame (#2885).
    [AvaloniaTest]
    public async Task Scene_time_commands_keep_a_time_left_as_shown_and_snap_a_typed_one([Values("30", "30000/1001")] string rateText)
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-scene-times");
        Scene scene = editor.Scene;
        scene.FindHierarchicalParent<Project>()!.FrameRate.Value = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        Rational rate = Rational.Parse(rateText, CultureInfo.InvariantCulture);
        scene.Start.Value = MediaTime.FromFrames(1, rate);
        scene.Duration.Value = MediaTime.FromFrames(301, rate);
        SceneSettingsTabExtension settings = SceneSettingsTabExtension.Instance;
        string typed = TimeSpan.FromTicks(MediaTime.FromFrames(600, rate).ToTicks(MidpointRounding.ToNegativeInfinity)).ToString();

        await RunAsync(settings, Execution("sceneSettings.changeSceneStart", editor, new ScriptedInteraction().Input(options => options.Value)));
        await RunAsync(settings, Execution("sceneSettings.changeSceneDuration", editor, new ScriptedInteraction().Input(typed)));
        MediaTime typedDuration = scene.Duration.Value;
        await RunAsync(settings, Execution("sceneSettings.changeSceneDuration", editor, new ScriptedInteraction().Input("00:00:00.0000001")));

        Assert.Multiple(() =>
        {
            Assert.That(scene.Start.Value, Is.EqualTo(MediaTime.FromFrames(1, rate)));
            Assert.That(typedDuration, Is.EqualTo(MediaTime.FromFrames(600, rate)));
            Assert.That(scene.Duration.Value, Is.EqualTo(typedDuration), "a tick snaps to frame zero, which is no duration");
        });
    }

    [TestCase("1920x1080", 1920, 1080)]
    [TestCase(" 1920 X 1080 ", 1920, 1080)]
    [TestCase("1920×1080", 1920, 1080)]
    [TestCase("1920,1080", 1920, 1080)]
    [TestCase("1920x0", 0, 0)]
    [TestCase("1920x", 0, 0)]
    [TestCase("99999999999x1080", 0, 0)]
    public void Scene_size_input_accepts_common_separators(string input, int width, int height)
    {
        bool parsed = SceneSettingsTabExtension.TryParseSize(input, out PixelSize size);

        Assert.That((parsed, size), Is.EqualTo((width > 0, new PixelSize(width, height))));
    }

    [AvaloniaTest]
    public async Task Preview_render_quality_is_picked_with_the_current_one_marked()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-preview-quality");
        IPreviewRenderQuality quality = editor.GetService<IPreviewRenderQuality>()!;
        RenderScale initial = quality.PreviewScale.Value;
        var interaction = new ScriptedInteraction().Pick<RenderScale>(item => item.Value == RenderScale.Quarter);

        await RunAsync(PreviewSettingsTabExtension.Instance,
            Execution("previewSettings.changePreviewRenderQuality", editor, interaction));

        Assert.Multiple(() =>
        {
            Assert.That(quality.PreviewScale.Value, Is.EqualTo(RenderScale.Quarter));
            Assert.That(interaction.PickedFrom<RenderScale>().Where(i => i.Description == Strings.Current)
                .Select(i => i.Value), Is.EqualTo(new[] { initial }));
        });
    }

    [AvaloniaTest]
    public async Task Picking_the_current_preview_quality_does_not_stop_playback()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-preview-quality-same");
        IPreviewRenderQuality quality = editor.GetService<IPreviewRenderQuality>()!;
        RenderScale current = quality.PreviewScale.Value;
        editor.Player.IsPlaying.Value = true;

        await RunAsync(PreviewSettingsTabExtension.Instance, Execution("previewSettings.changePreviewRenderQuality", editor,
            new ScriptedInteraction().Pick<RenderScale>(item => item.Value == current)));

        Assert.That((quality.PreviewScale.Value, editor.Player.IsPlaying.Value), Is.EqualTo((current, true)));
        editor.Player.IsPlaying.Value = false;
    }

    [AvaloniaTest]
    public async Task Preview_render_quality_stays_available_during_playback_and_pauses_first()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-preview-quality-playing");
        IPreviewRenderQuality quality = editor.GetService<IPreviewRenderQuality>()!;
        editor.Player.IsPlaying.Value = true;

        Assert.That(CanRun(PreviewSettingsTabExtension.Instance, Execution("previewSettings.changePreviewRenderQuality", editor)),
            Is.True, "A palette opened during playback would keep the entry disabled after playback stops.");
        await RunAsync(PreviewSettingsTabExtension.Instance, Execution("previewSettings.changePreviewRenderQuality", editor,
            new ScriptedInteraction().Pick<RenderScale>(item => item.Value == RenderScale.Half)));

        Assert.That((quality.PreviewScale.Value, editor.Player.IsPlaying.Value), Is.EqualTo((RenderScale.Half, false)));
    }

    [AvaloniaTest]
    public async Task Dock_layout_commands_save_rename_apply_and_delete()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-dock-layouts");
        var service = new DockLayoutPresetService(Path.Combine(
            BeutlHomeIsolation.CurrentHome!, $"dock-layout-presets-{Guid.NewGuid():N}.json"));
        var layouts = new DockLayoutTabExtension(service);
        Assert.That(CanRun(layouts, Execution("dockLayout.applyDockLayout", editor)), Is.False, "No layout saved yet.");

        await RunAsync(layouts, Execution("dockLayout.saveDockLayout", editor, new ScriptedInteraction()
            .Input(options => options.Value == Strings.DockLayout ? "Editing" : null)));
        await RunAsync(layouts, Execution("dockLayout.saveDockLayout", editor, new ScriptedInteraction().Input("Color")));
        await RunAsync(layouts, Execution("dockLayout.renameDockLayout", editor, new ScriptedInteraction()
            .Pick<DockLayoutPresetItem>(item => item.Label == "Editing")
            .Input(options =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(options.Validate!("color"), Is.EqualTo(Strings.CommandPalette_NameInUse));
                    Assert.That(options.Validate!("EDITING"), Is.Null, "A case-only change keeps the same layout.");
                });
                return "Cut";
            })));
        Assert.That(service.Items.Select(i => i.Name.Value), Is.EqualTo(new[] { "Cut", "Color" }));

        BeutlToolDockable library = editor.DockHost.Factory.EnumerateTools()
            .First(t => t.ToolContext.Extension is LibraryTabExtension);
        editor.DockHost.CloseToolTab(library.ToolContext);
        HeadlessTestHelpers.Settle();
        await RunAsync(layouts, Execution("dockLayout.applyDockLayout", editor, new ScriptedInteraction()
            .Pick<DockLayoutPresetItem>(item => item.Label == "Cut")));
        HeadlessTestHelpers.Settle();
        Assert.That(editor.DockHost.Factory.EnumerateTools().Any(t => t.ToolContext.Extension is LibraryTabExtension),
            Is.True);

        await RunAsync(layouts, Execution("dockLayout.deleteDockLayout", editor, new ScriptedInteraction()
            .Pick<DockLayoutPresetItem>(item => item.Label == "Color")));
        Assert.That(service.Items.Select(i => i.Name.Value), Is.EqualTo(new[] { "Cut" }));
    }

    [AvaloniaTest]
    public async Task Jump_to_history_lists_the_newest_state_first_and_jumps_to_the_picked_one()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-history");
        HistoryTabExtension history = HistoryTabExtension.Instance;
        editor.HistoryManager.Clear();
        Assert.That(CanRun(history, Execution("history.jumpToHistory", editor)), Is.False, "Only the initial state.");

        SceneSettingsTabExtension settings = SceneSettingsTabExtension.Instance;
        await RunAsync(settings, Execution("sceneSettings.changeSceneDuration", editor, new ScriptedInteraction().Input("00:00:05")));
        await RunAsync(settings, Execution("sceneSettings.changeSceneDuration", editor, new ScriptedInteraction().Input("00:00:06")));
        var interaction = new ScriptedInteraction()
            .Pick<HistoryEntry>(item => item.Value.IsInitial);

        await RunAsync(history, Execution("history.jumpToHistory", editor, interaction));

        IReadOnlyList<ContextCommandPickItem<HistoryEntry>> items = interaction.PickedFrom<HistoryEntry>();
        Assert.Multiple(() =>
        {
            Assert.That(editor.HistoryManager.CurrentIndex, Is.Zero);
            Assert.That(editor.Scene.Duration.Value, Is.Not.EqualTo(MediaTime.FromSeconds(6)));
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[^1].Value.IsInitial, Is.True, "Newest first, so the initial state is last.");
            Assert.That(items[0].Description, Does.EndWith(Strings.Current));
        });
    }

    [AvaloniaTest]
    public async Task Jump_to_history_can_revert_an_uncommitted_edit_to_the_initial_state()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-history-pending");
        HistoryTabExtension history = HistoryTabExtension.Instance;
        editor.HistoryManager.Clear();
        // Recorded but not committed, like a nudge still in its debounce.
        editor.Scene.Markers.Add(new SceneMarker(MediaTime.FromSeconds(1), "Pending"));
        Assert.That(editor.HistoryManager.HasPendingOperations, Is.True, "The marker must be a pending edit.");

        Assert.That(CanRun(history, Execution("history.jumpToHistory", editor)), Is.True);
        await RunAsync(history, Execution("history.jumpToHistory", editor,
            new ScriptedInteraction().Pick<HistoryEntry>(item => item.Value.IsInitial)));

        Assert.That(editor.Scene.Markers, Is.Empty);
    }

    [AvaloniaTest]
    public async Task A_cancelled_history_jump_leaves_the_history_alone()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-history-cancel");
        SceneSettingsTabExtension settings = SceneSettingsTabExtension.Instance;
        await RunAsync(settings, Execution("sceneSettings.changeSceneDuration", editor, new ScriptedInteraction().Input("00:00:05")));
        int current = editor.HistoryManager.CurrentIndex;
        // Cancelled as when the user switches editors after picking, while playback pauses.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await RunAsync(HistoryTabExtension.Instance, Execution("history.jumpToHistory", editor,
            new ScriptedInteraction(cancellation.Token).Pick<HistoryEntry>(item => item.Value.IsInitial)));

        Assert.That(editor.HistoryManager.CurrentIndex, Is.EqualTo(current));
    }

    [AvaloniaTest]
    public async Task Change_theme_picks_from_the_registered_themes()
    {
        await TestReset.ResetShellAsync();
        MainViewModel main = TestShell.MainViewModel;
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        string original = config.Theme.Value;
        // Two of its own, since which themes the registry holds depends on the tests that ran before.
        var theme = new ThemeDescriptor("palette-test-theme", "Palette test theme", ThemeVariant.Dark);
        var other = new ThemeDescriptor("palette-test-theme-2", "Palette test theme 2", ThemeVariant.Light);
        ThemeRegistry.Register(theme);
        ThemeRegistry.Register(other);
        try
        {
            Assert.That(MainViewExtension.Instance.CanRun(main, new ContextCommandExecution("main.changeTheme")), Is.True);
            await MainViewExtension.Instance.RunAsync(main, new ContextCommandExecution("main.changeTheme")
            {
                Interaction = new ScriptedInteraction().Pick<ThemeDescriptor>(item => item.Value == theme)
            });

            Assert.That(config.Theme.Value, Is.EqualTo(theme.Id));
        }
        finally
        {
            config.Theme.Value = original;
            ThemeRegistry.Unregister(theme);
            ThemeRegistry.Unregister(other);
        }
    }

    [AvaloniaTest]
    public async Task Open_recent_project_opens_the_picked_project()
    {
        await TestReset.ResetShellAsync();
        Project first = (await TestShell.Project.CreateProject(
            640, 480, new Rational(30), 44100, "palette-recent-a", TestShell.NewWorkspace("palette-recent-a")))!;
        string firstPath = first.Uri!.LocalPath;
        await TestShell.Project.CloseProjectAsync();
        await TestShell.Project.CreateProject(
            640, 480, new Rational(30), 44100, "palette-recent-b", TestShell.NewWorkspace("palette-recent-b"));
        HeadlessTestHelpers.Settle();

        MainViewModel main = TestShell.MainViewModel;
        var interaction = new ScriptedInteraction().Pick<string>(item => item.Value == firstPath);
        await MainViewExtension.Instance.RunAsync(
            main, new ContextCommandExecution("main.openRecentProject") { Interaction = interaction });
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(TestShell.Project.CurrentProject.Value?.Uri?.LocalPath, Is.EqualTo(firstPath));
            Assert.That(interaction.PickedFrom<string>().Single(i => i.Value == firstPath).Label,
                Is.EqualTo(Path.GetFileName(firstPath)));
        });
    }

    [AvaloniaTest]
    public async Task Tool_commands_run_from_the_palette_with_their_tab_closed()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-closed-tab");
        Assert.That(editor.DockHost.FindToolContext(typeof(SceneSettingsTabExtension)), Is.Null);
        CommandPaletteViewModel palette = TestShell.MainViewModel.CommandPalette;
        try
        {
            palette.Open();
            CommandPaletteItemViewModel item = palette.FilteredCommands.Single(i =>
                i.Command.Id == "sceneSettings.changeSceneDuration");
            Assert.That(item.IsEnabled, Is.True);
            palette.SelectedCommand.Value = item;

            Task operation = palette.ExecuteSelectedAsync();
            HeadlessTestHelpers.Settle();
            Assert.That(palette.Prompt.Value?.IsInput, Is.True);
            palette.Query.Value = "00:00:09";
            await palette.ExecuteSelectedAsync();
            await operation;

            Assert.Multiple(() =>
            {
                Assert.That(editor.Scene.Duration.Value, Is.EqualTo(MediaTime.FromSeconds(9)));
                Assert.That(editor.DockHost.FindToolContext(typeof(SceneSettingsTabExtension)), Is.Null);
                Assert.That(palette.IsOpen.Value, Is.False);
            });
        }
        finally
        {
            palette.Close();
        }
    }

    // Answers the palette's steps in order, refusing an answer the palette's validation would refuse.
    private sealed class ScriptedInteraction(CancellationToken cancellationToken = default) : IContextCommandInteraction
    {
        private readonly Queue<object> _steps = new();
        private readonly List<object> _pickedFrom = [];

        public CancellationToken CancellationToken => cancellationToken;

        public bool IsDone => _steps.Count == 0;

        public ScriptedInteraction Input(string? answer) => Input(_ => answer);

        public ScriptedInteraction Input(Func<ContextCommandInputOptions, string?> answer)
        {
            _steps.Enqueue(answer);
            return this;
        }

        public ScriptedInteraction Pick<T>(Func<ContextCommandPickItem<T>, bool> choose)
        {
            _steps.Enqueue(choose);
            return this;
        }

        public IReadOnlyList<ContextCommandPickItem<T>> PickedFrom<T>()
        {
            return _pickedFrom.OfType<IReadOnlyList<ContextCommandPickItem<T>>>().Last();
        }

        public Task<string?> ShowInputAsync(
            ContextCommandInputOptions options, CancellationToken cancellationToken = default)
        {
            var answer = (Func<ContextCommandInputOptions, string?>)_steps.Dequeue();
            string? value = answer(options);
            if (value is not null && options.Validate?.Invoke(value) is { } error)
                Assert.Fail($"The palette would refuse '{value}': {error}");

            return Task.FromResult(value);
        }

        public Task<ContextCommandPickItem<T>?> ShowQuickPickAsync<T>(
            IReadOnlyList<ContextCommandPickItem<T>> items,
            ContextCommandPickOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            _pickedFrom.Add(items);
            var choose = (Func<ContextCommandPickItem<T>, bool>)_steps.Dequeue();
            ContextCommandPickItem<T>? picked = items.FirstOrDefault(choose);
            Assert.That(picked, Is.Not.Null, "The scripted choice is not in the list.");
            return Task.FromResult(picked);
        }
    }
}
