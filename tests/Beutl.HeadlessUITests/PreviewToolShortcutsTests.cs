using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PathEditorTab.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Language;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using Beutl.ViewModels.SettingsPages;
using Beutl.Views;
using Dock.Model.Core;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public sealed class PreviewToolShortcutsTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task The_tool_keys_follow_focus_in_the_docked_and_floating_preview(bool floating)
    {
        (Window main, EditViewModel editor) = await OpenEditorWindow();
        Window previewWindow = main;
        try
        {
            if (floating)
            {
                BeutlDockFactory factory = editor.DockHost.Factory;
                PlayerToolDockable player = BeutlDockFactory.Traverse(editor.DockHost.Layout.Value)
                    .OfType<PlayerToolDockable>().Single();
                factory.SplitToWindow((IDock)player.Owner!, player, 20, 20, 600, 460, null);
                HeadlessTestHelpers.Render(3);
                previewWindow = (Window)factory.FindRoot(player, _ => true)!.Window!.Host!;
            }

            PlayerView preview = FindPreview(previewWindow);
            previewWindow.Activate();
            HeadlessTestHelpers.Click(preview.cameraToolButton);
            AssertTool(preview, PreviewTool.Camera3D);

            main.Activate();
            var timeline = main.GetVisualDescendants().OfType<TimelineTabView>().Single();
            var timelineModel = (TimelineTabViewModel)timeline.DataContext!;
            Assert.That(timeline.Focus(), Is.True);
            Press(main, PhysicalKey.C);
            Assert.That(timelineModel.IsRazorMode.Value, Is.True);
            Press(main, PhysicalKey.H);
            Press(main, PhysicalKey.V);
            Assert.That(timelineModel.IsRazorMode.Value, Is.False);
            AssertTool(preview, PreviewTool.Camera3D);

            editor.Player.SelectPreviewTool(PreviewTool.Hand);
            previewWindow.Activate();
            ClickFrame(previewWindow, preview);
            Assert.That(preview.framePanel.IsFocused, Is.True);
            Press(previewWindow, PhysicalKey.V);
            AssertTool(preview, PreviewTool.Move);
            Press(previewWindow, PhysicalKey.H);
            AssertTool(preview, PreviewTool.Hand);
            Press(previewWindow, PhysicalKey.V, RawInputModifiers.Shift);
            AssertTool(preview, PreviewTool.Camera3D);
            Press(previewWindow, PhysicalKey.C, RawInputModifiers.Shift);
            AssertTool(preview, PreviewTool.Crop);
        }
        finally
        {
            if (previewWindow != main) previewWindow.Close();
            await CloseEditorWindow(main);
        }
    }

    [AvaloniaTest]
    public async Task The_palette_lists_tools_and_tooltips_follow_keymap_changes()
    {
        (Window window, _) = await OpenEditorWindow();
        var palette = TestShell.MainViewModel.CommandPalette;
        try
        {
            PlayerView preview = FindPreview(window);
            var manager = TestShell.MainViewModel.ContextCommandManager;
            var keymap = new KeyMapSettingsPageViewModel(manager, TestShell.Extensions);
            string[] toolIds =
            [
                SceneEditorExtension.SelectPreviewMoveToolCommandId,
                SceneEditorExtension.SelectPreviewHandToolCommandId,
                SceneEditorExtension.SelectPreviewCropToolCommandId,
                SceneEditorExtension.SelectPreviewCameraToolCommandId
            ];
            var toolItems = keymap.Group.SelectMany(group => group.Items)
                .Where(item => toolIds.Contains(item.Command.Definition.Id)).ToArray();
            Assert.That(toolItems.Select(item => item.Command.Definition.Id), Is.EquivalentTo(toolIds));

            Assert.That(window.GetVisualDescendants().OfType<TimelineTabView>().Single().Focus(), Is.True);
            palette.Open();
            palette.Query.Value = Strings.Preview;
            HeadlessTestHelpers.Settle();
            var paletteTools = palette.FilteredCommands.Where(item => toolIds.Contains(item.Command.Id)).ToArray();
            Assert.That(paletteTools.Select(item => item.Command.Id), Is.EquivalentTo(toolIds));
            palette.SelectedCommand.Value = paletteTools.Single(item => item.Command.Id == SceneEditorExtension.SelectPreviewCameraToolCommandId);
            await palette.ExecuteSelectedAsync();
            AssertTool(preview, PreviewTool.Camera3D);

            var cameraKey = new KeyGesture(Key.V, KeyModifiers.Shift);
            AssertToolTip(preview.cameraToolButton, $"{Strings.Camera3DControl} ({cameraKey.ToString("p", null)})", capture: true);
            var hand = toolItems.Single(item => item.Command.Definition.Id == SceneEditorExtension.SelectPreviewHandToolCommandId);
            KeyGesture? original = hand.KeyGesture.Value;
            try
            {
                hand.SetKeyGesture(new KeyGesture(Key.F7));
                AssertToolTip(preview.handToolButton, $"{Strings.Hand} (F7)");
                ClickFrame(window, preview);
                Press(window, PhysicalKey.H);
                AssertTool(preview, PreviewTool.Camera3D);
                Press(window, PhysicalKey.F7);
                AssertTool(preview, PreviewTool.Hand);

                hand.SetKeyGesture(null);
                AssertToolTip(preview.handToolButton, Strings.Hand);
                Press(window, PhysicalKey.V);
                Press(window, PhysicalKey.F7);
                AssertTool(preview, PreviewTool.Move);
            }
            finally
            {
                hand.SetKeyGesture(original);
            }
        }
        finally
        {
            palette.Close();
            await CloseEditorWindow(window);
        }
    }

    [AvaloniaTest]
    public async Task Path_editing_and_text_entry_keep_their_keys_inside_the_preview()
    {
        using var fixture = await PathEditorInteractionTests.Fixture.Create();
        EditViewModel editor = fixture.Editor;
        editor.Player.PathEditor.FigureContext.Value = fixture.Model.FigureContext.Value;
        var preview = new PlayerView { DataContext = editor.Player };
        var host = new ContentControl { DataContext = editor, Content = preview };
        TestShell.MainViewModel.ContextCommandManager.Attach(host, SceneEditorExtension.Instance);
        fixture.Window.Content = host;
        try
        {
            HeadlessTestHelpers.Render(3);
            var path = preview.FindControl<PathEditorView>("pathEditorView")!;
            Assert.That(path.Focus(), Is.True);
            Press(fixture.Window, PhysicalKey.H);
            Assert.That(path.Tool, Is.EqualTo(PathEditorTool.Hand));
            Press(fixture.Window, PhysicalKey.V);
            Assert.That(path.Tool, Is.EqualTo(PathEditorTool.Move));
            Press(fixture.Window, PhysicalKey.C, RawInputModifiers.Shift);
            AssertTool(preview, PreviewTool.Move);

            var input = new TextBox();
            preview.framePanel.Children.Add(input);
            HeadlessTestHelpers.Render();
            Assert.That(input.Focus(), Is.True);
            Press(fixture.Window, PhysicalKey.H);
            fixture.Window.KeyTextInput("h");
            Assert.That(input.Text, Is.EqualTo("h"));
            AssertTool(preview, PreviewTool.Move);
        }
        finally
        {
            preview.DataContext = null;
            editor.Player.PathEditor.FigureContext.Value = null;
        }
    }

    [AvaloniaTest]
    public async Task Middle_button_pan_restores_the_selected_tool_when_released()
    {
        (Window window, _) = await OpenEditorWindow();
        try
        {
            PlayerView preview = FindPreview(window);
            HeadlessTestHelpers.Click(preview.cropToolButton);
            Point center = HeadlessTestHelpers.Center(preview.image, window);
            TimelineTestHost.WaitForHitTest(window, preview.framePanel, center);
            window.MouseDown(center, MouseButton.Middle);
            try
            {
                AssertTool(preview, PreviewTool.Hand);
                Press(window, PhysicalKey.V, RawInputModifiers.Shift);
                AssertTool(preview, PreviewTool.Hand);
            }
            finally
            {
                window.MouseUp(center, MouseButton.Middle);
            }
            AssertTool(preview, PreviewTool.Crop);
            Press(window, PhysicalKey.V, RawInputModifiers.Shift);
            AssertTool(preview, PreviewTool.Camera3D);
        }
        finally
        {
            await CloseEditorWindow(window);
        }
    }

    private static async Task<(Window Window, EditViewModel Editor)> OpenEditorWindow()
    {
        EditViewModel editor = (await TestShell.OpenNewSceneAsync("preview-tool-keys")).Editor;
        var view = new MainView { DataContext = TestShell.MainViewModel };
        var window = new Window { Content = view, DataContext = TestShell.MainViewModel, Width = 1080, Height = 750 };
        AppHelper.OverrideTopLevel = window;
        window.Show();
        HeadlessTestHelpers.Render(3);
        return (window, editor);
    }

    private static async Task CloseEditorWindow(Window window)
    {
        ((MainView)window.Content!).DataContext = null;
        window.Close();
        AppHelper.OverrideTopLevel = null;
        await TestReset.ResetShellAsync();
    }

    private static PlayerView FindPreview(Window window) => window.GetVisualDescendants().OfType<PlayerView>().Single();

    private static void ClickFrame(Window window, PlayerView preview)
    {
        Point center = HeadlessTestHelpers.Center(preview.image, window);
        TimelineTestHost.WaitForHitTest(window, preview.framePanel, center);
        HeadlessTestHelpers.Click(preview.image);
    }

    private static void Press(Window window, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPressQwerty(key, modifiers);
        window.KeyReleaseQwerty(key, modifiers);
        HeadlessTestHelpers.Render();
    }

    private static void AssertTool(PlayerView view, PreviewTool tool)
    {
        var player = (PlayerViewModel)view.DataContext!;
        (PreviewTool Tool, bool Active, bool? Checked)[] tools =
        [
            (PreviewTool.Move, player.IsMoveMode.Value, view.moveToolButton.IsChecked),
            (PreviewTool.Hand, player.IsHandMode.Value, view.handToolButton.IsChecked),
            (PreviewTool.Crop, player.IsCropMode.Value, view.cropToolButton.IsChecked),
            (PreviewTool.Camera3D, player.IsCameraMode.Value, view.cameraToolButton.IsChecked)
        ];
        Assert.Multiple(() =>
        {
            Assert.That(tools.Where(item => item.Active).Select(item => item.Tool), Is.EqualTo(new[] { tool }));
            Assert.That(tools.Where(item => item.Checked == true).Select(item => item.Tool), Is.EqualTo(new[] { tool }));
        });
    }

    private static void AssertToolTip(RadioButton button, string text, bool capture = false)
    {
        ToolTip.SetShouldUseOverlayLayer(button, true);
        TopLevel window = TopLevel.GetTopLevel(button)!;
        window.MouseMove(HeadlessTestHelpers.Center(button, window));
        ToolTip.SetIsOpen(button, true);
        try
        {
            HeadlessTestHelpers.Render(3);
            Assert.That(ToolTip.GetIsOpen(button), Is.True);
            Assert.That(ToolTip.GetTip(button), Is.EqualTo(text));
            if (capture) HeadlessCapture.SaveIfRequested(window, nameof(PreviewToolShortcutsTests), "camera-tool-tooltip");
        }
        finally
        {
            ToolTip.SetIsOpen(button, false);
        }
    }
}
