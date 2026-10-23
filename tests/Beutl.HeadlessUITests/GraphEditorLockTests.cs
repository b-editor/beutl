using System.Text.Json.Nodes;
using System.Windows.Input;
using Avalonia;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

// A locked clip makes the Graph Editor read-only. On a clip whose file recovery keeps, replacing or removing the easing
// that could not be read stays allowed, and that repairs the clip.
[TestFixture]
public class GraphEditorLockTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Drags_keys_and_commands_leave_a_locked_clips_keyframes_as_they_are(bool lockLayer)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        Element element = graph.Model.Element!;
        if (lockLayer) graph.Model.Scene.Layers.Add(new TimelineLayer { ZIndex = element.ZIndex, IsLocked = true });
        else element.IsLocked = true;
        graph.Model.HistoryManager.Commit();
        int undoCount = graph.Model.HistoryManager.UndoCount;
        string before = GraphScope.Snapshot(graph.Animation);
        Point key = graph.Position(graph.Second);
        Point handle = graph.Handle("ControlPoint2").TranslatePoint(default, graph.Window)!.Value;

        graph.Drag(key, key + new Vector(40, -40));
        graph.Drag(handle, handle + new Vector(-20, -20), RawInputModifiers.Alt);
        graph.View.Focus();
        graph.PressKey(Key.Right, RawInputModifiers.Alt);
        graph.PressKey(Key.F9);
        graph.PressKey(Key.R, RawInputModifiers.Alt);
        graph.PressKey(Key.Delete);
        graph.Model.DropEasing(new HoldEasing(), MediaTime.FromSeconds(1.5));
        graph.Model.InsertKeyFrame(new HoldEasing(), MediaTime.FromSeconds(1));
        graph.Model.ToggleUseGlobalClock();
        bool deleted = graph.Model.DeleteKeyFrames([graph.First]);
        GraphEditorKeyFrameViewModel second = graph.Model.SelectedView.Value!.KeyFrames.Single(x => x.Model == graph.Second);

        Assert.Multiple(() =>
        {
            Assert.That(GraphScope.Snapshot(graph.Animation), Is.EqualTo(before));
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undoCount));
            Assert.That(deleted, Is.False);
            Assert.That(graph.Model.CanEditSelection.Value, Is.False, "The easing, velocity and scale buttons are disabled.");
            Assert.That(graph.Model.CanReverseSelection.Value, Is.False);
            Assert.That(((ICommand)graph.Model.PasteKeyFrameAtCurrentPositionCommand).CanExecute(null), Is.False);
            Assert.That(((ICommand)second.RemoveCommand).CanExecute(null), Is.False);
            Assert.That(((ICommand)second.PasteCommand).CanExecute(null), Is.False);
            Assert.That(graph.Model.SelectedView.Value!.SelectionCount.Value, Is.EqualTo(2), "The selection stays.");
        });
    }

    [AvaloniaTest]
    [TestCase("drop")]
    [TestCase("delete")]
    [TestCase("menu")]
    [TestCase("tree")]
    public async Task On_a_protected_clip_only_the_unreadable_easing_is_replaced_or_removed_and_that_repairs_it(string action)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        Element element = graph.Model.Element!;
        var shape = (RectShape)element.Objects.Single();
        var height = new KeyFrameAnimation<float>();
        height.KeyFrames.Add(new KeyFrame<float> { KeyTime = MediaTime.Zero, Value = 50 });
        shape.Height.SetKeyFrameAnimation(height);
        KeyFrame<float> unreadable = CreateUnreadableKeyFrame(MediaTime.FromSeconds(1), 300);
        graph.Animation.KeyFrames.Insert(1, unreadable);
        graph.Model.SelectedView.Value!.SetSelection([]);
        element.SuppressedStorageSource = new SuppressedStorageSource(
            [], element.Uri ?? new Uri(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.belm")));
        graph.Model.HistoryManager.Commit();
        int undoCount = graph.Model.HistoryManager.UndoCount;
        string first = GraphScope.Snapshot(graph.First);
        using var tab = new GraphEditorTabViewModel(graph.Model.EditorContext);
        tab.Element.Value = element;
        GraphEditorTreeItemViewModel widthItem = Flatten(tab.TreeItems).Single(x => x.Property == shape.Width);
        GraphEditorTreeItemViewModel heightItem = Flatten(tab.TreeItems).Single(x => x.Property == shape.Height);
        GraphEditorKeyFrameViewModel firstItem = graph.Model.SelectedView.Value.KeyFrames.Single(x => x.Model == graph.First);
        GraphEditorKeyFrameViewModel unreadableItem = graph.Model.SelectedView.Value.KeyFrames.Single(x => x.Model == unreadable);
        Assert.That(SceneRecovery.HoldsRecoveryBlocker(unreadable), Is.True);

        // What loaded stays as it is, also when selected with the unreadable keyframe.
        graph.Model.DropEasing(new HoldEasing(), MediaTime.FromSeconds(0.5));
        bool deletedFirst = graph.Model.DeleteKeyFrames([graph.First]);
        tab.RemoveAnimation(heightItem);
        graph.Model.SelectedView.Value.SetSelection([unreadable, graph.First]);
        bool canRemoveMixed = ((ICommand)unreadableItem.RemoveCommand).CanExecute(null);
        graph.View.Focus();
        graph.PressKey(Key.Delete);
        graph.Model.SelectedView.Value.SetSelection([]);
        Assert.Multiple(() =>
        {
            Assert.That(GraphScope.Snapshot(graph.First), Is.EqualTo(first));
            Assert.That(graph.Animation.KeyFrames, Does.Contain(graph.First).And.Contain(unreadable));
            Assert.That(canRemoveMixed, Is.False);
            Assert.That(deletedFirst, Is.False);
            Assert.That(shape.Height.GetKeyFrameAnimation(), Is.SameAs(height));
            Assert.That(((ICommand)firstItem.RemoveCommand).CanExecute(null), Is.False);
            Assert.That(((ICommand)unreadableItem.RemoveCommand).CanExecute(null), Is.True);
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undoCount));
        });

        switch (action)
        {
            case "drop":
                graph.Model.DropEasing(new HoldEasing(), MediaTime.FromSeconds(1));
                Assert.That(unreadable.Easing, Is.TypeOf<HoldEasing>());
                break;
            case "delete":
                Assert.That(graph.Model.DeleteKeyFrames([unreadable]), Is.True);
                break;
            case "menu":
                unreadableItem.RemoveCommand.Execute();
                break;
            case "tree":
                tab.RemoveAnimation(widthItem);
                Assert.That(shape.Width.GetKeyFrameAnimation(), Is.Null);
                break;
        }

        Assert.Multiple(() =>
        {
            Assert.That(SceneRecovery.HoldsRecoveryBlocker(element), Is.False);
            Assert.That(element.SuppressedStorageSource, Is.Null, "The repair completes.");
            Assert.That(graph.Model.CanEdit.Value, Is.True);
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undoCount + 1));
        });
    }

    // A keyframe read back from JSON whose easing names a type that is not loaded.
    private static KeyFrame<float> CreateUnreadableKeyFrame(MediaTime keyTime, float value)
    {
        JsonObject json = CoreSerializer.SerializeToJsonObject(new KeyFrame<float> { KeyTime = keyTime, Value = value });
        json[nameof(KeyFrame.Easing)] = "[Missing.Plugin]Missing.Namespace:MissingEasing";
        return (KeyFrame<float>)CoreSerializer.DeserializeFromJsonObject(json, typeof(KeyFrame<float>));
    }

    private static IEnumerable<GraphEditorTreeItemViewModel> Flatten(IEnumerable<GraphEditorTreeItemViewModel> items)
        => items.SelectMany(item => Flatten(item.Children).Prepend(item));
}
