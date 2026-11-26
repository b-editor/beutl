using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.SceneSettingsTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Services.PrimitiveImpls;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using RectShape = Beutl.Graphics.Shapes.RectShape;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TimelineContextMenuTests
{
    [AvaloniaTest]
    public async Task Element_menu_groups_actions()
    {
        using TimelineTestHost host = await CreateHostAsync();
        var border = host.View.GetVisualDescendants().OfType<ElementView>().Single().FindControl<Border>("border")!;
        FAMenuFlyout menu = OpenMenu(border, "element");

        Assert.That(menu.Items.Cast<object>().Select(ItemText), Is.EqualTo(new string?[]
        {
            Strings.EnableElement, Strings.Lock, Strings.EditingMode, null,
            Strings.Split, Strings.SplitByCurrentFrame, Strings.AutoSplitBySilence, null,
            Strings.Cut, Strings.Copy, Strings.Duplicate, Strings.Delete, Strings.Rename, Strings.ChangeToOriginalLength, null,
            Strings.BulkSelection, Strings.GroupSelectedElements, Strings.UngroupSelectedElements, null,
            Strings.ChangeColor, Strings.DisableThumbnails, null,
            Strings.TrackMatte, GraphicsStrings.ClipTransition, null,
            Strings.SaveAsTemplate, null,
            Strings.Animation,
        }));
        FAMenuFlyoutItem duplicate = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Duplicate);
        Assert.That(duplicate.InputGesture, Is.EqualTo(new KeyGesture(Key.D, KeyGestureHelper.GetCommandModifier())));
        var groupGesture = new KeyGesture(Key.G, KeyGestureHelper.GetCommandModifier());
        Assert.That(FindItem(menu, Strings.GroupSelectedElements).InputGesture, Is.EqualTo(groupGesture));
        Assert.That(FindItem(menu, Strings.UngroupSelectedElements).InputGesture, Is.EqualTo(groupGesture));
        var original = host.Scene.Children.Single();
        Assert.That(host.Model.SelectedElements.Single().Model, Is.SameAs(original));
        Assert.That(duplicate.IsEffectivelyEnabled, Is.True);

        HeadlessTestHelpers.Click(duplicate, move: true);

        Assert.That(host.Scene.Children, Has.Count.EqualTo(2));
        Assert.That(host.Scene.Children, Does.Contain(original));
        Assert.That(menu.IsOpen, Is.False);
    }

    [AvaloniaTest]
    public async Task Duplicate_requires_an_eligible_clip_in_the_selection_or_its_groups()
    {
        using TimelineTestHost host = await CreateHostAsync(clipCount: 3);
        var locked = host.Scene.Children.Single(element => element.ZIndex.Value == 0);
        var alsoLocked = host.Scene.Children.Single(element => element.ZIndex.Value == 1);
        var editable = host.Scene.Children.Single(element => element.ZIndex.Value == 2);
        locked.IsLocked.Value = true;
        alsoLocked.IsLocked.Value = true;
        var lockedViewModel = host.Model.GetViewModelFor(locked)!;
        host.Model.SelectElement(lockedViewModel);
        host.Model.SelectElement(host.Model.GetViewModelFor(alsoLocked)!);
        HeadlessTestHelpers.Render(3);

        var border = host.View.GetVisualDescendants().OfType<ElementView>()
            .Single(view => ReferenceEquals(view.DataContext, lockedViewModel)).FindControl<Border>("border")!;
        FAMenuFlyout menu = OpenMenu(border);
        FAMenuFlyoutItem duplicate = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Duplicate);
        Assert.That(host.Model.SelectedElements.Select(item => item.Model), Is.EquivalentTo(new[] { locked, alsoLocked }));
        Assert.That(duplicate.IsEffectivelyEnabled, Is.False);
        menu.Hide();

        host.Scene.Groups.Add([locked.Id.Value, editable.Id.Value]);
        HeadlessTestHelpers.Render(3);
        OpenMenu(border);
        Assert.That(host.Model.SelectedElements.Select(item => item.Model), Is.EquivalentTo(new[] { locked, alsoLocked }));
        Assert.That(duplicate.IsEffectivelyEnabled, Is.True);

        HeadlessTestHelpers.Click(duplicate, move: true);
        Assert.That(host.Scene.Children, Has.Count.EqualTo(4));
    }

    [AvaloniaTest]
    public async Task Empty_area_menu_groups_actions()
    {
        using TimelineTestHost host = await CreateHostAsync();
        FAMenuFlyout menu = OpenMenu(host.Timeline, "empty-area", new Point(600, 100));

        Assert.That(menu.Items.Cast<object>().Select(ItemText), Is.EqualTo(new string?[]
        {
            Strings.AddElement, Strings.AddFromTemplate, Strings.AddAdjustmentLayer, null,
            Strings.EditingMode, null,
            Strings.Paste, Strings.SetStartTime, Strings.SetEndTime, Strings.Gaps, Strings.BulkSelection, null,
            SettingsStrings.AutoAdjustSceneDuration, Strings.TimelineSnap, null,
            Strings.SceneSettings, null,
            Strings.BpmGrid, null,
            Strings.TimelineZoom,
        }));

        var gaps = menu.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Text == Strings.Gaps);
        var closeGap = gaps.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.CloseGap);
        Assert.That(closeGap.InputGesture, Is.Null);
    }

    [AvaloniaTest]
    public async Task Scene_settings_opens_a_tab_and_the_main_menu_selects_the_same_tab()
    {
        using TimelineTestHost host = await CreateHostAsync();
        IEditorContext editor = host.Model.EditorContext;
        Assert.That(editor.FindToolTab<SceneSettingsTabViewModel>(), Is.Null);

        FAMenuFlyout menu = OpenMenu(host.Timeline, point: new Point(600, 100));
        HeadlessTestHelpers.Click(FindItem(menu, Strings.SceneSettings), move: true);
        var tab = editor.FindToolTab<SceneSettingsTabViewModel>();
        Assert.That(tab, Is.Not.Null);

        tab!.IsSelected.Value = false;
        TestShell.MainViewModel.MenuBar.ShowSceneSettings.Execute();
        Assert.That(editor.FindToolTab<SceneSettingsTabViewModel>(), Is.SameAs(tab));
        Assert.That(tab.IsSelected.Value, Is.True);
    }

    [AvaloniaTest]
    [TestCase(true, Key.OemOpenBrackets, "[")]
    [TestCase(false, Key.OemCloseBrackets, "]")]
    public async Task The_time_menu_uses_the_pointer_and_the_key_uses_the_playhead(bool isStart, Key key, string symbol)
    {
        using TimelineTestHost host = await CreateHostAsync();
        MediaTime pointer = MediaTime.FromSeconds(4);
        MediaTime playhead = MediaTime.FromSeconds(isStart ? 5 : 3);
        host.Model.Seek(playhead);

        var point = new Point(pointer.TimeToPixel(host.Model.Options.Value.Scale), 100);
        FAMenuFlyout menu = OpenMenu(host.Timeline, point: point);
        FAMenuFlyoutItem item = FindItem(menu, isStart ? Strings.SetStartTime : Strings.SetEndTime);
        Assert.That(item.InputGesture, Is.Null);
        HeadlessTestHelpers.Click(item, move: true);
        Assert.That(EditedFrame(), Is.EqualTo(pointer));

        // The app's ToolTabContent attaches these commands to the hosted view.
        TestShell.MainViewModel.ContextCommandManager!.Attach(host.View, TimelineTabExtension.Instance);
        Assert.That(host.View.Focus(), Is.True);
        host.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, symbol);
        host.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, symbol);
        HeadlessTestHelpers.Settle();
        Assert.That(EditedFrame(), Is.EqualTo(playhead));

        MediaTime EditedFrame() => isStart
            ? host.Scene.Start.Value
            : host.Scene.FromWindowTime(host.Scene.Duration.Value) - SceneTimeRangeService.GetFrameDuration(host.Scene);
    }

    private static FAMenuFlyoutItem FindItem(FAMenuFlyout menu, string text)
        => menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == text);

    private static string? ItemText(object item) => item switch
    {
        FAMenuFlyoutItem command => command.Text,
        FAMenuFlyoutSubItem submenu => submenu.Text,
        FAMenuFlyoutSeparator => null,
        _ => throw new InvalidOperationException($"Unexpected menu item type: {item.GetType().Name}"),
    };

    private static async Task<TimelineTestHost> CreateHostAsync(int clipCount = 1)
    {
        var clips = Enumerable.Range(0, clipCount)
            .Select(layer => new ElementDescription(MediaTime.FromSeconds(1), MediaTime.FromSeconds(2), layer,
                new ElementSource.EngineObject(() => new RectShape())))
            .ToArray();
        var host = await TimelineTestHost.CreateAsync("timeline-context-menu", MediaTime.FromSeconds(10),
            clips, theme: ThemeVariant.Dark);
        host.Window.Height = 880;
        HeadlessTestHelpers.Render(5);
        return host;
    }

    private static FAMenuFlyout OpenMenu(Control target, string? capture = null, Point? point = null)
    {
        var flyout = (FAMenuFlyout)target.ContextFlyout!;
        HeadlessTestHelpers.Click(target, point, MouseButton.Right, move: true, ticks: 5);
        Assert.That(flyout.IsOpen, Is.True);
        if (capture is not null)
            HeadlessCapture.SaveIfRequested(flyout.Popup.Child!, nameof(TimelineContextMenuTests), capture);
        return flyout;
    }
}
