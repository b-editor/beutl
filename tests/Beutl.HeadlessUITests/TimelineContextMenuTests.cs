using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Extensibility;
using Beutl.Language;
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
            Strings.Split, Strings.SplitByCurrentFrame, Strings.AutoSplitBySilenceMenu, null,
            Strings.Cut, Strings.Copy, Strings.Duplicate, Strings.Delete, Strings.RenameMenu, Strings.ChangeToOriginalLength, null,
            Strings.BulkSelection, Strings.GroupSelectedElements, Strings.UngroupSelectedElements, null,
            Strings.ChangeColor, Strings.DisableThumbnails, null,
            Strings.TrackMatte, GraphicsStrings.ClipTransition, null,
            Strings.SaveAsTemplateMenu, null,
            Strings.Animation,
        }));
        FAMenuFlyoutItem duplicate = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Duplicate);
        Assert.That(duplicate.InputGesture, Is.EqualTo(new KeyGesture(Key.D, KeyGestureHelper.GetCommandModifier())));
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
            Strings.Settings, null,
            Strings.BpmGrid, null,
            Strings.TimelineZoom,
        }));
    }

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

    [AvaloniaTest]
    public async Task Ruler_menu_shows_frame_cache_actions()
    {
        using TimelineTestHost host = await CreateHostAsync();
        FAMenuFlyout menu = OpenMenu(host.Ruler, "ruler");

        Assert.That(menu.Items.OfType<FAMenuFlyoutItem>().Select(item => (item.Text, item.Command)), Is.EqualTo(new (string, ICommand)[]
        {
            (Strings.DeleteAllCachedFrames, host.Model.DeleteAllFrameCache),
            (Strings.DeleteCachedFrameRange, host.Model.DeleteFrameCache),
            (Strings.LockCachedFrameRange, host.Model.LockFrameCache),
            (Strings.UnlockCachedFrameRange, host.Model.UnlockFrameCache),
        }));
    }
}
