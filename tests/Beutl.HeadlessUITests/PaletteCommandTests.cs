using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Configuration;
using Beutl.Editor;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Editor.Components.PreviewSettingsTab;
using Beutl.Editor.Components.SceneSettingsTab;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Graphics.AudioVisualizers;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Services;
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
    private static string NewWorkspace(string name)
    {
        string location = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(location);
        return location;
    }

    private static async Task<EditViewModel> OpenEditorForNewScene(string name)
    {
        await TestReset.ResetShellAsync();
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, NewWorkspace(name)))!;
        HeadlessTestHelpers.Settle();
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }

    private static ContextCommandExecution Execution(
        string commandName, IEditorContext editor, ScriptedInteraction? interaction = null)
    {
        return new ContextCommandExecution(commandName) { EditorContext = editor, Interaction = interaction };
    }

    private static void SetPlayhead(EditViewModel editor, double seconds)
    {
        editor.GetService<IEditorClock>()!.CurrentTime.Value = TimeSpan.FromSeconds(seconds);
    }

    private static TimeSpan Playhead(EditViewModel editor) => editor.GetService<IEditorClock>()!.CurrentTime.Value;

    private static async Task<Element> AddRectAsync(EditViewModel editor, double start, double length, int layer)
    {
        ElementAddResult result = await editor.GetService<IElementAdder>()!.AddAsync(
            [
                new ElementDescription(
                    TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(length), layer,
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

        await editor.ExecuteAsync(Execution("GotoTimecode", editor, interaction));

        Assert.That(Playhead(editor), Is.EqualTo(TimeSpan.FromSeconds(2)));
        Assert.That(interaction.IsDone, Is.True);
    }

    [TestCase(0, 30, "00:00:00.000")]
    [TestCase(1, 120, "00:00:00.008")]
    [TestCase(119, 120, "00:00:00.991")]
    [TestCase(30 * 3600 * 23 + 15, 30, "23:00:00.500")]
    [TestCase(30 * 3600 * 25, 30, "1.01:00:00.000")]
    public void Palette_timecodes_parse_back_to_the_same_frame(long frame, int rate, string expected)
    {
        TimeSpan time = TimeSpan.FromSeconds(frame / (double)rate).RoundToRate(rate);
        string text = CommandPaletteInput.FormatTimecode(time);

        bool parsed = GotoTimecodeParser.TryParse(text, rate, TimeSpan.Zero, [], out TimeSpan result, out _);

        Assert.That((text, parsed, result.RoundToRate(rate)), Is.EqualTo((expected, true, time)));
    }

    [AvaloniaTest]
    public async Task Accepting_the_prefilled_timecode_keeps_a_frame_milliseconds_cannot_name()
    {
        await TestReset.ResetShellAsync();
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 999, 44100, "palette-goto-999fps", NewWorkspace("palette-goto-999fps")))!;
        HeadlessTestHelpers.Settle();
        TestShell.Editor.ActivateTabItem(project.Items.OfType<Scene>().First());
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
        // 501 / 999 s prints as 00:00:00.501, which snaps back to frame 500.
        TimeSpan frame501 = TimeSpan.FromSeconds(501 / 999d).RoundToRate(999);
        editor.GetService<IEditorClock>()!.CurrentTime.Value = frame501;
        HeadlessTestHelpers.Settle();
        string? prefilled = null;

        await editor.ExecuteAsync(Execution("GotoTimecode", editor,
            new ScriptedInteraction().Input(options => prefilled = options.Value)));

        Assert.That((prefilled, Playhead(editor)), Is.EqualTo(("00:00:00.501", frame501)));
    }

    [AvaloniaTest]
    public async Task Goto_timecode_from_a_key_still_opens_the_inline_editor()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-goto-timecode-key");
        int requests = 0;
        using IDisposable subscription = editor.Player.BeginEditTimecodeRequested.Subscribe(_ => requests++);

        await editor.ExecuteAsync(new ContextCommandExecution("GotoTimecode"));

        Assert.That(requests, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task Marker_commands_add_rename_and_seek_without_the_timeline()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-markers");
        Scene scene = editor.Scene;
        Assert.That(editor.CanExecute(Execution("GoToMarker", editor)), Is.False, "No marker to go to yet.");

        SetPlayhead(editor, 1);
        await editor.ExecuteAsync(Execution("AddMarker", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.That(options.Validate!("  "), Is.EqualTo(Strings.CommandPalette_ValueRequired));
            return options.Value == "Marker 1" ? "Opening" : null;
        })));
        Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.AddMarker));

        // The same frame keeps one marker, renamed instead of duplicated.
        await editor.ExecuteAsync(Execution("AddMarker", editor,
            new ScriptedInteraction().Input(options => options.Value == "Opening" ? "Intro" : null)));
        SetPlayhead(editor, 3);
        await editor.ExecuteAsync(Execution("AddMarker", editor, new ScriptedInteraction().Input("Outro")));
        Assert.That(scene.Markers.Select(m => (m.Time, m.Name)), Is.EqualTo(new[]
        {
            (TimeSpan.FromSeconds(1), "Intro"),
            (TimeSpan.FromSeconds(3), "Outro"),
        }));

        SetPlayhead(editor, 0);
        var goTo = new ScriptedInteraction().Pick<SceneMarker>(item => item.Label == "Outro");
        await editor.ExecuteAsync(Execution("GoToMarker", editor, goTo));
        Assert.Multiple(() =>
        {
            Assert.That(Playhead(editor), Is.EqualTo(TimeSpan.FromSeconds(3)));
            Assert.That(goTo.PickedFrom<SceneMarker>().Select(i => (i.Label, i.Description)), Is.EqualTo(new[]
            {
                ("Intro", "00:00:01.000"),
                ("Outro", "00:00:03.000"),
            }));
        });

        await editor.ExecuteAsync(Execution("RenameMarker", editor, new ScriptedInteraction()
            .Pick<SceneMarker>(item => item.Label == "Intro")
            .Input(options => options.Value == "Intro" ? "Cold open" : null)));
        Assert.Multiple(() =>
        {
            Assert.That(scene.Markers[0].Name, Is.EqualTo("Cold open"));
            Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.EditMarker));
        });

        await editor.UndoAsync();
        Assert.That(scene.Markers[0].Name, Is.EqualTo("Intro"));
    }

    [AvaloniaTest]
    public async Task Rename_split_and_save_as_template_act_on_the_selected_element_with_the_timeline_closed()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-element");
        TimelineTabExtension timeline = TimelineTabExtension.Instance;
        Element element = await AddRectAsync(editor, 0, 4, 0);
        CloseTimeline(editor);
        Assert.That(timeline.CanExecute(Execution("Rename", editor)), Is.False, "Nothing is selected yet.");

        editor.GetService<IEditorSelection>()!.SelectedObject.Value = element;
        await timeline.ExecuteAsync(Execution("Rename", editor, new ScriptedInteraction()
            .Input(options => options.Value == element.Name ? "Title" : null)));
        Assert.Multiple(() =>
        {
            Assert.That(element.Name, Is.EqualTo("Title"));
            Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.RenameElement));
        });

        SetPlayhead(editor, 4);
        Assert.That(timeline.CanExecute(Execution("Split", editor)), Is.False, "The playhead is past the element.");
        SetPlayhead(editor, 1);
        await timeline.ExecuteAsync(Execution("Split", editor));
        Assert.That(editor.Scene.Children.Select(e => (e.Start, e.Length)), Is.EquivalentTo(new[]
        {
            (TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            (TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)),
        }));

        // Saving a template is refused for a name that cannot be a file name; cancelling saves nothing.
        int templates = ObjectTemplateService.Instance.FindByBaseType(typeof(Element)).Count();
        await timeline.ExecuteAsync(Execution("SaveAsTemplate", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.That(options.Validate!("a/b"), Is.EqualTo(MessageStrings.InvalidString));
            return null;
        })));
        Assert.That(ObjectTemplateService.Instance.FindByBaseType(typeof(Element)).Count(), Is.EqualTo(templates));

        element.IsLocked = true;
        Assert.Multiple(() =>
        {
            Assert.That(timeline.CanExecute(Execution("Rename", editor)), Is.False);
            Assert.That(timeline.CanExecute(Execution("Split", editor)), Is.False);
            Assert.That(timeline.CanExecute(Execution("SaveAsTemplate", editor)), Is.True);
        });
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

            ((ElementViewModel)view.DataContext!).RenameRequested();
            HeadlessTestHelpers.Render(5);
            Assert.That(textBox.IsFocused, Is.True, "Rename did not focus the editor, so typing would go elsewhere.");
            textBox.SelectAll();
            window.KeyTextInput("Ti");
            window.KeyTextInput("tle");
            Assert.That(element.Name, Is.Not.EqualTo("Title"), "Each keystroke must not write the name.");

            elsewhere.Focus();
            HeadlessTestHelpers.Render(2);

            Assert.Multiple(() =>
            {
                Assert.That(element.Name, Is.EqualTo("Title"));
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
        editor.GetService<IEditorSelection>()!.SelectedObject.Value = second;
        SetPlayhead(editor, 2);

        await TimelineTabExtension.Instance.ExecuteAsync(Execution("Split", editor));
        HeadlessTestHelpers.Settle();

        Assert.That(editor.Scene.Children, Has.Count.EqualTo(4));
    }

    [AvaloniaTest]
    public async Task Add_element_puts_the_picked_library_item_at_the_playhead_above_the_visible_clips()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-add-element");
        await AddRectAsync(editor, 0, 4, 0);
        CloseTimeline(editor);
        SetPlayhead(editor, 1);

        var interaction = new ScriptedInteraction().Pick<Type>(item => item.Value == typeof(EllipseShape));
        await LibraryTabExtension.Instance.ExecuteAsync(Execution("AddElement", editor, interaction));
        HeadlessTestHelpers.Settle();

        Element added = editor.Scene.Children.Single(e => e.Objects.OfType<EllipseShape>().Any());
        Assert.Multiple(() =>
        {
            Assert.That(added.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(added.ZIndex, Is.EqualTo(1));
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

        await settings.ExecuteAsync(Execution("ChangeSceneSize", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(options.Value, Is.EqualTo("640x480"));
                Assert.That(options.Validate!("0x480"), Is.EqualTo(Strings.CommandPalette_SceneSizeInvalid));
                Assert.That(options.Validate!("wide"), Is.EqualTo(Strings.CommandPalette_SceneSizeInvalid));
            });
            return "1280 × 720";
        })));
        await settings.ExecuteAsync(Execution("ChangeSceneDuration", editor, new ScriptedInteraction().Input(options =>
        {
            Assert.That(options.Validate!("00:00:00"), Is.EqualTo(MessageStrings.ValueLessThanOrEqualToZero));
            return "00:00:07";
        })));
        await settings.ExecuteAsync(Execution("ChangeSceneStart", editor, new ScriptedInteraction().Input("00:00:01")));

        Assert.Multiple(() =>
        {
            Assert.That(scene.FrameSize, Is.EqualTo(new PixelSize(1280, 720)));
            Assert.That(scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(7)));
            Assert.That(scene.Start, Is.EqualTo(TimeSpan.FromSeconds(1)));
            Assert.That(LastHistoryName(editor), Is.EqualTo(CommandNames.ChangeSceneSettings));
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

        await PreviewSettingsTabExtension.Instance.ExecuteAsync(
            Execution("ChangePreviewRenderQuality", editor, interaction));

        Assert.Multiple(() =>
        {
            Assert.That(quality.PreviewScale.Value, Is.EqualTo(RenderScale.Quarter));
            Assert.That(interaction.PickedFrom<RenderScale>().Where(i => i.Description == Strings.Current)
                .Select(i => i.Value), Is.EqualTo(new[] { initial }));
        });
    }

    [AvaloniaTest]
    public async Task Dock_layout_commands_save_rename_apply_and_delete()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-dock-layouts");
        var service = new DockLayoutPresetService(Path.Combine(
            BeutlHomeIsolation.CurrentHome!, $"dock-layout-presets-{Guid.NewGuid():N}.json"));
        var layouts = new DockLayoutTabExtension(service);
        Assert.That(layouts.CanExecute(Execution("ApplyDockLayout", editor)), Is.False, "No layout saved yet.");

        await layouts.ExecuteAsync(Execution("SaveDockLayout", editor, new ScriptedInteraction()
            .Input(options => options.Value == Strings.DockLayout ? "Editing" : null)));
        await layouts.ExecuteAsync(Execution("SaveDockLayout", editor, new ScriptedInteraction().Input("Color")));
        await layouts.ExecuteAsync(Execution("RenameDockLayout", editor, new ScriptedInteraction()
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
        await layouts.ExecuteAsync(Execution("ApplyDockLayout", editor, new ScriptedInteraction()
            .Pick<DockLayoutPresetItem>(item => item.Label == "Cut")));
        HeadlessTestHelpers.Settle();
        Assert.That(editor.DockHost.Factory.EnumerateTools().Any(t => t.ToolContext.Extension is LibraryTabExtension),
            Is.True);

        await layouts.ExecuteAsync(Execution("DeleteDockLayout", editor, new ScriptedInteraction()
            .Pick<DockLayoutPresetItem>(item => item.Label == "Color")));
        Assert.That(service.Items.Select(i => i.Name.Value), Is.EqualTo(new[] { "Cut" }));
    }

    [AvaloniaTest]
    public async Task Jump_to_history_lists_the_newest_state_first_and_jumps_to_the_picked_one()
    {
        EditViewModel editor = await OpenEditorForNewScene("palette-history");
        HistoryTabExtension history = HistoryTabExtension.Instance;
        editor.HistoryManager.Clear();
        Assert.That(history.CanExecute(Execution("JumpToHistory", editor)), Is.False, "Only the initial state.");

        SceneSettingsTabExtension settings = SceneSettingsTabExtension.Instance;
        await settings.ExecuteAsync(Execution("ChangeSceneDuration", editor, new ScriptedInteraction().Input("00:00:05")));
        await settings.ExecuteAsync(Execution("ChangeSceneDuration", editor, new ScriptedInteraction().Input("00:00:06")));
        var interaction = new ScriptedInteraction()
            .Pick<HistoryEntry>(item => item.Value.IsInitial);

        await history.ExecuteAsync(Execution("JumpToHistory", editor, interaction));

        IReadOnlyList<ContextCommandPickItem<HistoryEntry>> items = interaction.PickedFrom<HistoryEntry>();
        Assert.Multiple(() =>
        {
            Assert.That(editor.HistoryManager.CurrentIndex, Is.Zero);
            Assert.That(editor.Scene.Duration, Is.Not.EqualTo(TimeSpan.FromSeconds(6)));
            Assert.That(items, Has.Count.EqualTo(3));
            Assert.That(items[^1].Value.IsInitial, Is.True, "Newest first, so the initial state is last.");
            Assert.That(items[0].Description, Does.EndWith(Strings.Current));
        });
    }

    [AvaloniaTest]
    public async Task Change_theme_picks_from_the_registered_themes()
    {
        await TestReset.ResetShellAsync();
        MainViewModel main = TestShell.MainViewModel;
        ViewConfig config = GlobalConfiguration.Instance.ViewConfig;
        string original = config.Theme;
        // Two of its own, since which themes the registry holds depends on the tests that ran before.
        var theme = new ThemeDescriptor("palette-test-theme", "Palette test theme", ThemeVariant.Dark);
        var other = new ThemeDescriptor("palette-test-theme-2", "Palette test theme 2", ThemeVariant.Light);
        ThemeRegistry.Register(theme);
        ThemeRegistry.Register(other);
        try
        {
            Assert.That(main.CanExecute(new ContextCommandExecution("ChangeTheme")), Is.True);
            await main.ExecuteAsync(new ContextCommandExecution("ChangeTheme")
            {
                Interaction = new ScriptedInteraction().Pick<ThemeDescriptor>(item => item.Value == theme)
            });

            Assert.That(config.Theme, Is.EqualTo(theme.Id));
        }
        finally
        {
            config.Theme = original;
            ThemeRegistry.Unregister(theme);
            ThemeRegistry.Unregister(other);
        }
    }

    [AvaloniaTest]
    public async Task Open_recent_project_opens_the_picked_project()
    {
        await TestReset.ResetShellAsync();
        Project first = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "palette-recent-a", NewWorkspace("palette-recent-a")))!;
        string firstPath = first.Uri!.LocalPath;
        await TestShell.Project.CloseProjectAsync();
        await TestShell.Project.CreateProject(
            640, 480, 30, 44100, "palette-recent-b", NewWorkspace("palette-recent-b"));
        HeadlessTestHelpers.Settle();

        MainViewModel main = TestShell.MainViewModel;
        var interaction = new ScriptedInteraction().Pick<string>(item => item.Value == firstPath);
        await main.ExecuteAsync(new ContextCommandExecution("OpenRecentProject") { Interaction = interaction });
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
                i.Command.Id == $"{typeof(SceneSettingsTabExtension).FullName}.ChangeSceneDuration");
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
                Assert.That(editor.Scene.Duration, Is.EqualTo(TimeSpan.FromSeconds(9)));
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
    private sealed class ScriptedInteraction : IContextCommandInteraction
    {
        private readonly Queue<object> _steps = new();
        private readonly List<object> _pickedFrom = [];

        public CancellationToken CancellationToken => CancellationToken.None;

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
