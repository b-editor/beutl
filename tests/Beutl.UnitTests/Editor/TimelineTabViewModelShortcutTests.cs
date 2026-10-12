using Avalonia.Controls;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Editor;

[TestFixture]
[NonParallelizable]
public class TimelineTabViewModelShortcutTests
{
    [Test]
    public void IsTextInputSource_ReturnsTrue_ForTextBox()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(new TextBox()), Is.True);
    }

    [Test]
    public void IsTextInputSource_ReturnsFalse_ForNonTextVisual()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(new Border()), Is.False);
    }

    [Test]
    public void IsTextInputSource_ReturnsFalse_ForNull()
    {
        Assert.That(TimelineTabViewModel.IsTextInputSource(null), Is.False);
    }

    [Test]
    public void CloseGapCommand_FlushesPendingNudgeBeforeGapService()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_command", duration: TimeSpan.FromSeconds(30));
        Element anchor = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        var nudgeService = new CaptureNudgeService();
        var gapService = new CaptureGapService(nudgeService);
        using TimelineTabViewModel viewModel = TimelineTabViewModelTests.CreateViewModel(
            harness.Scene, nudgeService: nudgeService, gapService: gapService);
        viewModel.SelectedElements.Add(viewModel.Elements.Single(element => element.Model == anchor));

        viewModel.CloseGap.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(nudgeService.FlushCount, Is.EqualTo(1));
            Assert.That(gapService.CloseGapCalled, Is.True);
            Assert.That(gapService.CloseGapSawFlush, Is.True);
        });
    }

    [Test]
    public void CloseGapCommand_NoSelection_NotifiesWithoutCallingGapService()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_command", duration: TimeSpan.FromSeconds(30));
        var nudgeService = new CaptureNudgeService();
        var gapService = new CaptureGapService(nudgeService);
        using TimelineTabViewModel viewModel = TimelineTabViewModelTests.CreateViewModel(
            harness.Scene, nudgeService: nudgeService, gapService: gapService);
        using var notifications = NotificationCapture.Install();

        viewModel.CloseGap.Execute();

        Notification notification = notifications.All.Single();
        Assert.Multiple(() =>
        {
            Assert.That(gapService.CloseGapCalled, Is.False);
            Assert.That(nudgeService.FlushCount, Is.Zero);
            Assert.That(notification.Title, Is.EqualTo(Strings.CloseGap));
            Assert.That(notification.Message, Is.EqualTo(Strings.NoElementSelected));
        });
    }

    [Test]
    public void FindGapNavigationTarget_Forward_ReturnsGapCenter()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.Zero, forward: true)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(4)));
    }

    [Test]
    public void FindGapNavigationTarget_Forward_NoGapAhead_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.FromSeconds(10), forward: true)?.Target;

        Assert.That(target, Is.Null);
    }

    [Test]
    public void FindGapNavigationTarget_Forward_CurrentBeforeSceneStart_FindsFirstInRangeGap()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_nav",
            start: TimeSpan.FromSeconds(10),
            duration: TimeSpan.FromSeconds(20));
        harness.AddElement(TimeSpan.FromSeconds(11), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.Zero, forward: true)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(14)));
    }

    [Test]
    public void FindGapNavigationTarget_Forward_CurrentBeforeSceneStart_ReturnsBoundaryTouchingGap()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_nav",
            start: TimeSpan.FromSeconds(10),
            duration: TimeSpan.FromSeconds(20));
        // Ends exactly at the scene start, so the gap [10s, 14s] begins on the boundary. It lies
        // wholly in range, so navigating forward from before the scene must land on it.
        harness.AddElement(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(1));
        harness.AddElement(TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(2));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.Zero, forward: true)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(12)));
    }

    [Test]
    public void FindGapNavigationTarget_Backward_CurrentBeyondSceneEnd_ReturnsBoundaryTouchingGap()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(20));
        harness.AddElement(TimeSpan.Zero, TimeSpan.FromSeconds(5));
        harness.AddElement(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5));
        // Starts exactly at the scene end, so the gap [15s, 20s] ends on the boundary. It lies wholly
        // in range, so navigating back from beyond the scene must land on it, not skip to [5s, 10s].
        harness.AddElement(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(5));

        TimeSpan? target = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.FromSeconds(100), forward: false)?.Target;

        Assert.That(target, Is.EqualTo(TimeSpan.FromSeconds(17.5)));
    }

    [Test]
    public void FindGapNavigationTarget_NoElements_ReturnsNull()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(
                TimelineTabViewModel.FindGapNavigationTarget(harness.Scene, TimeSpan.Zero, forward: true),
                Is.Null);
            Assert.That(
                TimelineTabViewModel.FindGapNavigationTarget(harness.Scene, TimeSpan.Zero, forward: false),
                Is.Null);
        });
    }

    [Test]
    public void FindGapNavigationTarget_ReturnsGapAnchorForSelection()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_nav", duration: TimeSpan.FromSeconds(30));
        Element a = harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));

        var result = TimelineTabViewModel.FindGapNavigationTarget(harness.Scene, TimeSpan.Zero, forward: true);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Target, Is.EqualTo(TimeSpan.FromSeconds(4)));
            // The anchor is the element ending at the gap start, so GoToGap selects it and a follow-up
            // Close Gap closes this gap.
            Assert.That(result?.Anchor, Is.SameAs(a));
        });
    }

    [Test]
    public void FindGapNavigationTarget_ClippedGapStart_DoesNotSelectStaleAnchor()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_nav",
            start: TimeSpan.FromSeconds(50),
            duration: TimeSpan.FromSeconds(30));
        // The gap 10s-100s straddles the active range 50s-80s; its start is clipped to 50s. The anchor
        // ends off-scene at 10s, so navigation must not select it (Close Gap would collapse 10s-100s).
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        harness.AddElement(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(5));

        var result = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.FromSeconds(40), forward: true);

        Assert.Multiple(() =>
        {
            // Target is the centre of the visible portion (50s-80s); the scroll uses the gap's layer.
            Assert.That(result?.Target, Is.EqualTo(TimeSpan.FromSeconds(65)));
            Assert.That(result?.ZIndex, Is.EqualTo(0));
            Assert.That(result?.Anchor, Is.Null);
        });
    }

    [Test]
    public void FindGapNavigationTarget_ClippedGapEnd_DoesNotSelectAnchor()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_nav",
            start: TimeSpan.FromSeconds(50),
            duration: TimeSpan.FromSeconds(50));
        // The gap 90s-120s straddles the active range 50s-100s; its end is clipped to 100s. The anchor
        // (85s clip) abuts the visible start, but closing the gap would move the off-scene 120s clip,
        // so navigation must not select it.
        harness.AddElement(TimeSpan.FromSeconds(85), TimeSpan.FromSeconds(5));
        harness.AddElement(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(5));

        var result = TimelineTabViewModel.FindGapNavigationTarget(
            harness.Scene, TimeSpan.FromSeconds(60), forward: true);

        Assert.Multiple(() =>
        {
            Assert.That(result?.Target, Is.EqualTo(TimeSpan.FromSeconds(95)));
            Assert.That(result?.Anchor, Is.Null);
        });
    }

    [Test]
    public void ResolvePointerGapToClose_OnlyReturnsGapWhollyInsideScene()
    {
        using var harness = new SceneHistoryHarness(
            "beutl_timeline_gap_ptr",
            start: TimeSpan.FromSeconds(50),
            duration: TimeSpan.FromSeconds(50));
        // Active scene is 50s-100s. Layer 0 has a gap fully inside (60s-70s); layer 1 has one that
        // straddles the scene end (90s-120s). Only the fully-inside gap may be closed by a click.
        harness.AddElement(TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(5), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(5), zIndex: 0);
        harness.AddElement(TimeSpan.FromSeconds(85), TimeSpan.FromSeconds(5), zIndex: 1);
        harness.AddElement(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(5), zIndex: 1);

        Assert.Multiple(() =>
        {
            Assert.That(
                TimelineTabViewModel.ResolvePointerGapToClose(harness.Scene, TimeSpan.FromSeconds(65), layer: 0)?.Range,
                Is.EqualTo(new TimeRange(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10))));
            // Clicking the visible part of a gap that runs past the scene end must not close it.
            Assert.That(
                TimelineTabViewModel.ResolvePointerGapToClose(harness.Scene, TimeSpan.FromSeconds(95), layer: 1),
                Is.Null);
            // A click beyond the scene end lands in the straddling gap but is rejected.
            Assert.That(
                TimelineTabViewModel.ResolvePointerGapToClose(harness.Scene, TimeSpan.FromSeconds(110), layer: 1),
                Is.Null);
        });
    }

    [Test]
    public void CloseAllGapsCommand_FlushesPendingNudgeBeforeGapService()
    {
        using var harness = new SceneHistoryHarness("beutl_timeline_gap_command", duration: TimeSpan.FromSeconds(30));
        harness.AddElement(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        harness.AddElement(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2));
        var nudgeService = new CaptureNudgeService();
        var gapService = new CaptureGapService(nudgeService);
        using TimelineTabViewModel viewModel = TimelineTabViewModelTests.CreateViewModel(
            harness.Scene, nudgeService: nudgeService, gapService: gapService);

        viewModel.CloseAllGaps.Execute();

        Assert.Multiple(() =>
        {
            Assert.That(nudgeService.FlushCount, Is.EqualTo(1));
            Assert.That(gapService.CloseAllGapsCalled, Is.True);
            Assert.That(gapService.CloseAllGapsSawFlush, Is.True);
        });
    }

    private sealed class CaptureNudgeService : IElementNudgeService
    {
        public int FlushCount { get; private set; }

        public void Nudge(Scene scene, IReadOnlyList<Element> targets, int frames)
        {
        }

        public void Flush()
        {
            FlushCount++;
        }

        public void Dispose()
        {
        }
    }

    private sealed class CaptureGapService(CaptureNudgeService nudgeService) : IElementGapService
    {
        public bool CloseGapCalled { get; private set; }
        public bool CloseGapSawFlush { get; private set; }
        public bool CloseAllGapsCalled { get; private set; }
        public bool CloseAllGapsSawFlush { get; private set; }

        public bool CloseGapAfter(Scene scene, Element anchor)
        {
            CloseGapCalled = true;
            CloseGapSawFlush = nudgeService.FlushCount > 0;
            return true;
        }

        public int CloseAllGaps(Scene scene)
        {
            CloseAllGapsCalled = true;
            CloseAllGapsSawFlush = nudgeService.FlushCount > 0;
            return 1;
        }
    }
}
