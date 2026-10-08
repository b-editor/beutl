using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using GraphicsPoint = Beutl.Graphics.Point;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorSelectionCommandTests
{
    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Distribute_preserves_endpoints_values_selection_and_one_undo(bool keyboard)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        var third = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.7), Value = 200 };
        var fourth = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1.1), Value = 300 };
        var unselected = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = 900 };
        graph.Animation.KeyFrames.Add(third, out _);
        graph.Animation.KeyFrames.Add(fourth, out _);
        graph.Animation.KeyFrames.Add(unselected, out _);
        graph.Model.SelectedView.Value!.SetSelection([graph.First, third, fourth, graph.Second]);
        graph.Model.HistoryManager.Commit();
        string before = Serialize(graph.Animation);
        int undo = graph.Model.HistoryManager.UndoCount;

        Invoke(graph, "DistributeEvenly", keyboard ? Key.D : null);

        Assert.That(graph.Animation.KeyFrames.Select(key => key.KeyTime.TotalSeconds * 30),
            Is.EqualTo(new[] { 15d, 25, 35, 45, 60 }).Within(0.00001));
        Assert.That(graph.Animation.KeyFrames.Select(key => key.Value), Is.EqualTo(new[] { 100f, 200, 300, 500, 900 }));
        Assert.That(graph.Model.SelectedView.Value!.SelectionCount.Value, Is.EqualTo(4));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        string after = Serialize(graph.Animation);
        graph.Model.HistoryManager.Undo();
        Assert.That(Serialize(graph.Animation), Is.EqualTo(before));
        graph.Model.HistoryManager.Redo();
        Assert.That(Serialize(graph.Animation), Is.EqualTo(after));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Reverse_mirrors_times_and_directional_easing_with_one_undo(bool keyboard)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        graph.Second.Easing = new QuadraticEaseIn();
        graph.Model.HistoryManager.Commit();
        string before = Serialize(graph.Animation);
        int undo = graph.Model.HistoryManager.UndoCount;

        Invoke(graph, "Reverse", keyboard ? Key.R : null);

        Assert.That(graph.Animation.KeyFrames, Is.EqualTo(new[] { graph.Second, graph.First }));
        Assert.That(graph.First.KeyTime.TotalSeconds, Is.EqualTo(1.5));
        Assert.That(graph.Second.KeyTime.TotalSeconds, Is.EqualTo(0.5));
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Second.Value, Is.EqualTo(500));
        Assert.That(graph.First.Easing, Is.TypeOf<QuadraticEaseOut>());
        Assert.That(graph.Model.SelectedView.Value!.SelectionCount.Value, Is.EqualTo(2));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        string after = Serialize(graph.Animation);
        graph.Model.HistoryManager.Undo();
        Assert.That(Serialize(graph.Animation), Is.EqualTo(before));
        graph.Model.HistoryManager.Redo();
        Assert.That(Serialize(graph.Animation), Is.EqualTo(after));
    }

    [AvaloniaTest]
    [TestCase(3, false)]
    [TestCase(3, true)]
    [TestCase(4, false)]
    [TestCase(4, true)]
    public async Task Reverse_multiple_keys_keeps_sorted_order_and_mirrors_interpolation(int count, bool keyboard)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        for (int index = 1; index < count - 1; index++)
        {
            graph.Animation.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = TimeSpan.FromSeconds(0.5 + (double)index / (count - 1)),
                Value = 100 + 50 * index,
                Easing = new SplineEasing(0.25f, 0.1f, 0.75f, 0.9f)
            }, out _);
        }
        var keys = graph.Animation.KeyFrames.ToArray();
        graph.Model.SelectedView.Value!.SetSelection(keys);
        graph.Model.HistoryManager.Commit();
        var sampleTimes = Enumerable.Range(0, 31).Select(index => TimeSpan.FromSeconds(0.5 + index / 30d)).ToArray();
        var originalValues = sampleTimes.Select(graph.Animation.Interpolate).ToArray();
        string before = Serialize(graph.Animation);
        int undo = graph.Model.HistoryManager.UndoCount;

        Invoke(graph, "Reverse", keyboard ? Key.R : null);

        Assert.Multiple(() =>
        {
            Assert.That(graph.Animation.KeyFrames, Is.EqualTo(keys.Reverse()));
            Assert.That(graph.Animation.KeyFrames.Select(key => key.KeyTime), Is.Ordered);
            Assert.That(sampleTimes.Select(graph.Animation.Interpolate), Is.EqualTo(originalValues.Reverse()).Within(0.01));
            Assert.That(graph.Model.SelectedView.Value!.SelectionCount.Value, Is.EqualTo(count));
            Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        });
        string after = Serialize(graph.Animation);
        for (int replay = 0; replay < 2; replay++)
        {
            graph.Model.HistoryManager.Undo();
            Assert.That(Serialize(graph.Animation), Is.EqualTo(before));
            Assert.That(graph.Animation.KeyFrames, Is.EqualTo(keys));
            Assert.That(sampleTimes.Select(graph.Animation.Interpolate), Is.EqualTo(originalValues).Within(0.01));
            graph.Model.HistoryManager.Redo();
            Assert.That(Serialize(graph.Animation), Is.EqualTo(after));
            Assert.That(graph.Animation.KeyFrames, Is.EqualTo(keys.Reverse()));
            Assert.That(sampleTimes.Select(graph.Animation.Interpolate), Is.EqualTo(originalValues.Reverse()).Within(0.01));
        }
    }

    [AvaloniaTest]
    [TestCase("distribute-collision")]
    [TestCase("distribute-rounding")]
    [TestCase("reverse-collision")]
    [TestCase("reverse-hold")]
    public async Task Rejected_retiming_does_not_change_any_key_or_history(string reason)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        string action = reason.StartsWith("distribute") ? "DistributeEvenly" : "Reverse";
        if (reason == "reverse-hold") graph.Second.Easing = new HoldEasing();
        else
        {
            var third = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.7), Value = 200 };
            graph.Animation.KeyFrames.Add(third, out _);
            graph.Model.SelectedView.Value!.SetSelection([graph.First, third, graph.Second]);
            if (reason == "distribute-rounding")
            {
                third.KeyTime = TimeSpan.FromSeconds(0.51);
                graph.Second.KeyTime = TimeSpan.FromSeconds(0.52);
            }
            else graph.Animation.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = TimeSpan.FromSeconds(reason == "reverse-collision" ? 1.3 : 1),
                Value = 999
            }, out _);
        }
        graph.Model.HistoryManager.Commit();
        string before = Serialize(graph.Animation);
        int undo = graph.Model.HistoryManager.UndoCount;

        Invoke(graph, action);

        Assert.That(Serialize(graph.Animation), Is.EqualTo(before));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        Assert.That(graph.Model.HistoryManager.HasPendingOperations, Is.False);
    }

    [AvaloniaTest]
    [TestCase(2d)]
    [TestCase(0d)]
    [TestCase(-0.5d)]
    public async Task Value_scale_only_changes_selected_values_and_is_one_undo(double factor)
    {
        using var graph = await GraphScope.CreateAsync(separateHandles: true);
        // Value scaling must keep even imported subframe times exactly as they are.
        graph.First.KeyTime = TimeSpan.FromSeconds(0.501);
        var unselected = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(2), Value = 999 };
        graph.Animation.KeyFrames.Add(unselected);
        graph.Model.HistoryManager.Commit();
        string before = Serialize(graph.Animation);
        int undo = graph.Model.HistoryManager.UndoCount;

        graph.View.ScaleSelectionValues(factor);

        // Width uses the property's existing nonnegative validation.
        Assert.That(graph.First.Value, Is.EqualTo(Math.Max(0, 100 * factor)));
        Assert.That(graph.Second.Value, Is.EqualTo(Math.Max(0, 500 * factor)));
        Assert.That(graph.First.KeyTime.TotalSeconds, Is.EqualTo(0.501));
        Assert.That(graph.Second.KeyTime.TotalSeconds, Is.EqualTo(1.5));
        Assert.That(unselected.Value, Is.EqualTo(999));
        Assert.That(unselected.KeyTime.TotalSeconds, Is.EqualTo(2));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        string after = Serialize(graph.Animation);
        graph.Model.HistoryManager.Undo();
        Assert.That(Serialize(graph.Animation), Is.EqualTo(before));
        graph.Model.HistoryManager.Redo();
        Assert.That(Serialize(graph.Animation), Is.EqualTo(after));
    }

    [AvaloniaTest]
    [TestCase(2d, true)]
    [TestCase(-0.5d, true)]
    [TestCase(2d, false)]
    [TestCase(-0.5d, false)]
    public async Task Vector_value_scale_preserves_the_other_channel(double factor, bool selectAll)
    {
        using var graph = await GraphScope.CreateAsync();
        var animation = new KeyFrameAnimation<GraphicsPoint>();
        var first = new KeyFrame<GraphicsPoint> { Value = new GraphicsPoint(10, 20) };
        var second = new KeyFrame<GraphicsPoint>
        {
            KeyTime = TimeSpan.FromSeconds(1),
            Value = new GraphicsPoint(30, 40),
            Easing = new SplineEasing(0.25f, 0.25f, 0.75f, 0.75f)
        };
        animation.KeyFrames.Add(first);
        animation.KeyFrames.Add(second);
        var effect = new StrokeEffect();
        effect.Offset.Animation = animation;
        var shape = graph.Model.Element!.Objects.OfType<RectShape>().Single();
        ((FilterEffectGroup)shape.FilterEffect.CurrentValue!).Children.Add(effect);
        graph.Model.HistoryManager.Commit();
        using var model = new GraphEditorViewModel<GraphicsPoint>(graph.Model.EditorContext, animation, graph.Model.Element);
        graph.View.DataContext = model;
        model.SelectedView.Value!.SetSelection(selectAll ? animation.KeyFrames : [first]);
        var sampleTimes = Enumerable.Range(0, 31).Select(index => TimeSpan.FromSeconds(index / 30d)).ToArray();
        var originalValues = sampleTimes.Select(animation.Interpolate).ToArray();
        string before = Serialize(animation);
        int undo = model.HistoryManager.UndoCount;

        graph.View.ScaleSelectionValues(factor);

        Assert.That(first.Value, Is.EqualTo(new GraphicsPoint((float)(10 * factor), 20)));
        double endX = selectAll ? 30 * factor : 30;
        Assert.That(second.Value, Is.EqualTo(new GraphicsPoint((float)endX, 40)));
        var scaledValues = sampleTimes.Select(animation.Interpolate).ToArray();
        Assert.That(scaledValues.Select(value => value.Y), Is.EqualTo(originalValues.Select(value => value.Y)).Within(0.0001));
        for (int index = 0; index < scaledValues.Length; index++)
        {
            double progress = (originalValues[index].Y - 20) / 20;
            Assert.That(scaledValues[index].X, Is.EqualTo(10 * factor + (endX - 10 * factor) * progress).Within(0.0001));
        }
        Assert.That(model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        string after = Serialize(animation);
        model.HistoryManager.Undo();
        Assert.That(Serialize(animation), Is.EqualTo(before));
        Assert.That(sampleTimes.Select(animation.Interpolate), Is.EqualTo(originalValues));
        model.HistoryManager.Redo();
        Assert.That(Serialize(animation), Is.EqualTo(after));
        Assert.That(sampleTimes.Select(animation.Interpolate), Is.EqualTo(scaledValues));
        graph.View.DataContext = graph.Model;
    }

    [AvaloniaTest]
    public async Task Retiming_shortcuts_do_not_run_while_a_textbox_has_focus()
    {
        using var graph = await GraphScope.CreateAsync();
        var third = new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(0.7), Value = 200 };
        graph.Animation.KeyFrames.Add(third, out _);
        graph.Model.SelectedView.Value!.SetSelection(graph.Animation.KeyFrames);
        graph.Model.HistoryManager.Commit();
        var textBox = new TextBox { Text = "edit", Width = 100 };
        graph.View.FindControl<Panel>("graphPanel")!.Children.Add(textBox);
        HeadlessTestHelpers.Render();
        textBox.Focus();
        string before = Serialize(graph.Animation);
        int undo = graph.Model.HistoryManager.UndoCount;
        foreach (Key key in new[] { Key.D, Key.R })
        {
            graph.Window.KeyPress(key, RawInputModifiers.Alt, PhysicalKey.None, null);
            graph.Window.KeyRelease(key, RawInputModifiers.Alt, PhysicalKey.None, null);
        }
        Assert.That(Serialize(graph.Animation), Is.EqualTo(before));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Scale_picker_changes_values_only_after_confirmation(bool confirm)
    {
        using var graph = await GraphScope.CreateAsync(light: confirm);
        int undo = graph.Model.HistoryManager.UndoCount;
        Invoke(graph, "ScaleValues");
        var flyout = graph.View.ValueScaleFlyout!;
        Assert.That(flyout.IsOpen, Is.True);
        var presenter = (Control)flyout.Popup.Child!;
        presenter.GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 2;
        HeadlessTestHelpers.Render(3);
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        if (Environment.GetEnvironmentVariable("BEUTL_GRAPH_CONTEXT_CAPTURE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            using var frame = TopLevel.GetTopLevel(presenter)!.CaptureRenderedFrame();
            frame?.Save(Path.Combine(directory, $"value-scale-{confirm}.png"), PngBitmapEncoderOptions.Default);
        }
        presenter.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == (confirm ? "AcceptButton" : "DismissButton"))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();
        Assert.That(flyout.IsOpen, Is.False);
        Assert.That(graph.First.Value, Is.EqualTo(confirm ? 200 : 100));
        Assert.That(graph.Second.Value, Is.EqualTo(confirm ? 1000 : 500));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo + (confirm ? 1 : 0)));
        Assert.That(graph.Model.HistoryManager.HasPendingOperations, Is.False);
    }

    [AvaloniaTest]
    [TestCase("selection")]
    [TestCase("channel")]
    [TestCase("detach")]
    [TestCase("edit")]
    public async Task Scale_picker_is_cancelled_if_its_context_changes(string change)
    {
        using var graph = await GraphScope.CreateAsync();
        graph.View.ShowValueScaleFlyout();
        var flyout = graph.View.ValueScaleFlyout!;
        ((Control)flyout.Popup.Child!).GetVisualDescendants().OfType<NumericUpDown>().Single().Value = 2;
        int undo = graph.Model.HistoryManager.UndoCount;
        if (change == "selection") graph.Model.SelectedView.Value!.SetSelection([graph.First]);
        else if (change == "channel") graph.Model.SelectedView.Value = null;
        else if (change == "detach") graph.View.DataContext = null;
        else graph.Second.Value = 501;
        Assert.That(flyout.IsOpen, Is.False);
        Assert.That(graph.First.Value, Is.EqualTo(100));
        Assert.That(graph.Second.Value, Is.EqualTo(change == "edit" ? 501 : 500));
        Assert.That(graph.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
    }

    [AvaloniaTest]
    public async Task Selection_menu_disables_operations_without_enough_keys()
    {
        using var graph = await GraphScope.CreateAsync(selectAll: false);
        graph.BackgroundMenu.ShowAt(graph.View.FindControl<Panel>("graphPanel")!);
        HeadlessTestHelpers.Render();
        var menu = graph.BackgroundMenu.Items.OfType<FAMenuFlyoutItem>().Where(item => item.Tag is string)
            .ToDictionary(item => (string)item.Tag!);
        Assert.That(menu["DistributeEvenly"].IsEnabled, Is.False);
        Assert.That(menu["Reverse"].IsEnabled, Is.False);
        Assert.That(menu["ScaleValues"].IsEnabled, Is.False);
        graph.Model.SelectedView.Value!.SetSelection([graph.First]);
        Assert.That(menu["ScaleValues"].IsEnabled, Is.True);
        Assert.That(menu["Reverse"].IsEnabled, Is.False);
        graph.Model.SelectedView.Value!.SetSelection([graph.First, graph.Second]);
        Assert.That(menu["Reverse"].IsEnabled, Is.True);
        Assert.That(menu["DistributeEvenly"].IsEnabled, Is.False);
    }

    private static string Serialize(IKeyFrameAnimation animation) => CoreSerializer.SerializeToJsonObject((KeyFrameAnimation)animation).ToJsonString();

    private static void Invoke(GraphScope graph, string action, Key? shortcut = null)
    {
        HeadlessTestHelpers.Render();
        if (shortcut is { } key)
        {
            graph.View.Focus();
            graph.Window.KeyPress(key, RawInputModifiers.Alt, PhysicalKey.None, null);
            graph.Window.KeyRelease(key, RawInputModifiers.Alt, PhysicalKey.None, null);
        }
        else
        {
            graph.BackgroundMenu.ShowAt(graph.View.FindControl<Panel>("graphPanel")!);
            HeadlessTestHelpers.Render();
            graph.BackgroundMenu.Items.OfType<FAMenuFlyoutItem>().Single(item => Equals(item.Tag, action))
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            graph.BackgroundMenu.Hide();
        }
        HeadlessTestHelpers.Render(3);
    }
}
