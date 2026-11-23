using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using GraphicsPoint = Beutl.Graphics.Point;

namespace Beutl.HeadlessUITests;

// The clip's marks of its objects' keyframes and expressions, its "Animation" menu that opens their inline layers, and
// the values an inline layer draws.
[TestFixture]
public class TimelineAnimationMarksTests
{
    private static readonly MediaTime s_start = MediaTime.FromSeconds(1);

    private static KeyFrameAnimation<T> Animation<T>(params (double Seconds, T Value)[] keys)
    {
        var animation = new KeyFrameAnimation<T>();
        foreach ((double seconds, T value) in keys)
        {
            animation.KeyFrames.Add(new KeyFrame<T>
            {
                KeyTime = { Value = MediaTime.FromSeconds(seconds) },
                Value = { Value = value },
                Easing = { Value = new SplineEasing() }
            });
        }

        return animation;
    }

    // A rectangle whose width has keys at 0.5 s and 1.5 s of its own time, whose position, a pair of properties, has keys
    // at 1 s, and whose fill's opacity has a key on the scene's clock at 2.75 s, 1.75 s into a clip that starts at 1 s.
    private static RectShape CreateAnimatedShape()
    {
        var shape = new RectShape();
        shape.Width.SetKeyFrameAnimation(Animation((0.5, 100f), (1.5, 300f)));
        var translate = new TranslateTransform();
        translate.X.SetKeyFrameAnimation(Animation((1.0, 0f)));
        translate.Y.SetKeyFrameAnimation(Animation((1.0, 50f)));
        shape.Transform.Value = translate;
        var fill = new SolidColorBrush(Colors.Orange);
        KeyFrameAnimation<float> opacity = Animation((2.75, 40f));
        opacity.UseGlobalClock.Value = true;
        fill.Opacity.SetKeyFrameAnimation(opacity);
        shape.Fill.Value = fill;
        return shape;
    }

    private static ElementDescription Clip(Func<EngineObject> create, double startSeconds = 1, double lengthSeconds = 4, int layer = 0)
        => new(MediaTime.FromSeconds(startSeconds), MediaTime.FromSeconds(lengthSeconds), layer,
            new ElementSource.EngineObject(create));

    private static MediaTime[] Seconds(params double[] seconds) => [.. seconds.Select(MediaTime.FromSeconds)];

    private static HistoryManager History(TimelineTestHost host)
        => host.Model.EditorContext.GetRequiredService<HistoryManager>();

    private static ElementView ViewOf(TimelineTestHost host, Element element)
        => host.View.GetVisualDescendants().OfType<ElementView>()
            .Single(view => view.DataContext is ElementViewModel model && model.Model == element);

    // Each property's toggle in a menu, with the submenus above it as "Object/Property".
    private static List<(string Path, FAToggleMenuFlyoutItem Item)> Toggles(System.Collections.IEnumerable items, string prefix = "")
    {
        var toggles = new List<(string Path, FAToggleMenuFlyoutItem Item)>();
        foreach (object? item in items)
        {
            switch (item)
            {
                case FAToggleMenuFlyoutItem toggle:
                    toggles.Add((prefix + toggle.Text, toggle));
                    break;
                case FAMenuFlyoutSubItem submenu:
                    toggles.AddRange(Toggles(submenu.Items, $"{prefix}{submenu.Text}/"));
                    break;
            }
        }

        return toggles;
    }

    [AvaloniaTest]
    public async Task The_marks_hold_each_key_time_of_the_objects_from_the_clip_start_once()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "animation-marks-times", MediaTime.FromSeconds(10), [Clip(CreateAnimatedShape)]);
        HeadlessTestHelpers.Render(5);
        Element element = host.Scene.Children.Single();
        ElementViewModel model = host.Model.FindViewModelFor(element)!;
        ElementView view = ViewOf(host, element);

        ElementAnimationMarks marks = model.AnimationMarks.Value;
        var keyFrameMarks = view.FindControl<ElementKeyFrameMarks>("keyFrameMarks")!;
        Border clip = view.FindControl<Border>("border")!;
        Assert.Multiple(() =>
        {
            Assert.That(marks.HasKeyFrames, Is.True);
            Assert.That(marks.HasExpression, Is.False);
            // The two keys of the position are at one time, and the fill's key on the scene's clock is 1.75 s into the clip.
            Assert.That(marks.Times, Is.EqualTo(Seconds(0.5, 1, 1.5, 1.75)));
            Assert.That(keyFrameMarks.Times, Is.SameAs(marks.Times));
            // The marks span the clip's border too, so a time is at the x it has on the clip, the clip's end included.
            Assert.That(keyFrameMarks.Bounds.Width, Is.EqualTo(clip.Bounds.Width).Within(0.01));
            Assert.That(keyFrameMarks.TranslatePoint(default, clip)!.Value.X, Is.EqualTo(0).Within(0.01));
            Assert.That(view.FindControl<Control>("keyFrameGlyph")!.IsVisible, Is.True);
            Assert.That(view.FindControl<Control>("expressionGlyph")!.IsVisible, Is.False);
        });
    }

    [AvaloniaTest]
    public async Task The_marks_follow_keys_added_moved_and_removed_and_expressions()
    {
        RectShape? shape = null;
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "animation-marks-edits", MediaTime.FromSeconds(10), [Clip(() => shape = CreateAnimatedShape())]);
        HeadlessTestHelpers.Render(5);
        Element element = host.Scene.Children.Single();
        ElementViewModel model = host.Model.FindViewModelFor(element)!;
        ElementView view = ViewOf(host, element);
        HistoryManager history = History(host);
        var width = (KeyFrameAnimation<float>)shape!.Width.GetKeyFrameAnimation()!;

        // An added key raises no Edited, only the list of keys changes.
        history.ExecuteInTransaction(() => width.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(3) },
            Value = { Value = 10 }
        }), "add");
        HeadlessTestHelpers.Settle();
        Assert.That(model.AnimationMarks.Value.Times, Is.EqualTo(Seconds(0.5, 1, 1.5, 1.75, 3)));

        history.ExecuteInTransaction(() => width.KeyFrames[0].KeyTime.Value = MediaTime.FromSeconds(0.25), "move");
        HeadlessTestHelpers.Settle();
        Assert.That(model.AnimationMarks.Value.Times, Is.EqualTo(Seconds(0.25, 1, 1.5, 1.75, 3)));

        history.ExecuteInTransaction(() => width.KeyFrames.RemoveAt(2), "remove a key");
        HeadlessTestHelpers.Settle();
        Assert.That(model.AnimationMarks.Value.Times, Is.EqualTo(Seconds(0.25, 1, 1.5, 1.75)));

        // Moving the clip moves keys on its own clock with it, and leaves a key on the scene's clock where it is.
        history.ExecuteInTransaction(() => element.Start.Value = MediaTime.FromSeconds(2), "move the clip");
        HeadlessTestHelpers.Settle();
        Assert.That(model.AnimationMarks.Value.Times, Is.EqualTo(Seconds(0.25, 0.75, 1, 1.5)));

        history.ExecuteInTransaction(() => shape.Width.SetKeyFrameAnimation(null), "remove the animation");
        history.ExecuteInTransaction(() => ((TranslateTransform)shape.Transform.Value!).X.SetKeyFrameAnimation(null), "x");
        history.ExecuteInTransaction(() => ((TranslateTransform)shape.Transform.Value!).Y.SetKeyFrameAnimation(null), "y");
        history.ExecuteInTransaction(() => ((SolidColorBrush)shape.Fill.Value!).Opacity.SetKeyFrameAnimation(null), "fill");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(model.AnimationMarks.Value, Is.EqualTo(ElementAnimationMarks.None));
            Assert.That(view.FindControl<Control>("animationGlyphs")!.IsVisible, Is.False);
        });

        // Removing the last key leaves the animation in place, which counts for nothing until a key comes back.
        var empty = new KeyFrameAnimation<float>();
        history.ExecuteInTransaction(() => shape.Width.SetKeyFrameAnimation(empty), "empty");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(model.AnimationMarks.Value, Is.EqualTo(ElementAnimationMarks.None));
            Assert.That(model.GetAnimationMenu(), Is.Empty);
        });
        history.ExecuteInTransaction(() => empty.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = { Value = MediaTime.FromSeconds(0.5) },
            Value = { Value = 1 }
        }), "first key");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(model.AnimationMarks.Value.HasKeyFrames, Is.True);
            Assert.That(model.AnimationMarks.Value.Times, Is.EqualTo(Seconds(0.5)));
            Assert.That(model.GetAnimationMenu().SelectMany(row => row.Adapters).Select(row => row.GetEngineProperty()),
                Is.EqualTo(new[] { shape.Width }));
        });
        history.ExecuteInTransaction(() => shape.Width.SetKeyFrameAnimation(null), "remove it again");
        HeadlessTestHelpers.Settle();

        history.ExecuteInTransaction(() => shape.Opacity.Expression = Expression.Create<float>("50"), "expression");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(model.AnimationMarks.Value.HasExpression, Is.True);
            Assert.That(model.AnimationMarks.Value.HasKeyFrames, Is.False);
            Assert.That(view.FindControl<Control>("expressionGlyph")!.IsVisible, Is.True);
            Assert.That(view.FindControl<Control>("keyFrameGlyph")!.IsVisible, Is.False);
        });

        // Keys added under the expression (#4458) show as keys too.
        history.ExecuteInTransaction(() => shape.Opacity.SetKeyFrameAnimation(Animation((2.0, 0f))), "keys under it");
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(model.AnimationMarks.Value.HasExpression, Is.True);
            Assert.That(model.AnimationMarks.Value.Times, Is.EqualTo(Seconds(2)));
        });
    }

    [AvaloniaTest]
    public async Task An_edit_reads_the_marks_of_its_own_clip_only_and_a_clip_off_screen_reads_none()
    {
        RectShape? first = null;
        RectShape? second = null;
        RectShape? far = null;
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "animation-marks-scope", MediaTime.FromSeconds(120),
            [
                Clip(() => first = CreateAnimatedShape(), layer: 0),
                Clip(() => second = CreateAnimatedShape(), layer: 1),
                Clip(() => far = CreateAnimatedShape(), startSeconds: 100, layer: 2),
            ]);
        HeadlessTestHelpers.Render(5);
        ElementViewModel firstModel = host.Model.FindViewModelFor((Element)first!.HierarchicalParent!)!;
        ElementViewModel secondModel = host.Model.FindViewModelFor((Element)second!.HierarchicalParent!)!;
        // Off screen it has a view model, but no clip to show marks on.
        ElementViewModel farModel = host.Model.GetViewModelFor((Element)far!.HierarchicalParent!)!;
        int firstReads = firstModel.AnimationMarksReadCount;
        int secondReads = secondModel.AnimationMarksReadCount;
        HistoryManager history = History(host);

        history.ExecuteInTransaction(() => first.Width.GetKeyFrameAnimation()!.KeyFrames[0].KeyTime.Value = MediaTime.FromSeconds(0.25), "first");
        history.ExecuteInTransaction(() => far.Width.GetKeyFrameAnimation()!.KeyFrames[0].KeyTime.Value = MediaTime.FromSeconds(0.25), "far");
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(firstReads, Is.GreaterThan(0));
            Assert.That(firstModel.AnimationMarksReadCount, Is.EqualTo(firstReads + 1));
            Assert.That(firstModel.AnimationMarks.Value.Times[0], Is.EqualTo(MediaTime.FromSeconds(0.25)));
            Assert.That(secondModel.AnimationMarksReadCount, Is.EqualTo(secondReads));
            Assert.That(farModel.AnimationMarksReadCount, Is.Zero);
        });
    }

    [AvaloniaTest]
    public async Task The_animation_menu_opens_and_closes_inline_layers_without_doubling_them()
    {
        RectShape? shape = null;
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "animation-marks-menu", MediaTime.FromSeconds(10), [Clip(() => shape = CreateAnimatedShape())]);
        HeadlessTestHelpers.Render(5);
        Element element = host.Scene.Children.Single();
        ElementView view = ViewOf(host, element);
        var translate = (TranslateTransform)shape!.Transform.Value!;
        var fill = (SolidColorBrush)shape.Fill.Value!;
        PropertyPair pair = PropertyPair.Find(translate.X)!;
        var widthAdapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        var opacityAdapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)fill.Opacity, fill);

        (FAMenuFlyoutItem ShowAll, List<(string Path, FAToggleMenuFlyoutItem Item)> Toggles) Open()
        {
            var flyout = (FAMenuFlyout)view.FindControl<Border>("border")!.ContextFlyout!;
            flyout.Hide();
            flyout.ShowAt(view);
            HeadlessTestHelpers.Render(3);
            var menu = view.FindControl<FAMenuFlyoutSubItem>("animationMenu")!;
            return (menu.Items.OfType<FAMenuFlyoutItem>().First(), Toggles(menu.Items));
        }

        void Click(FAMenuFlyoutItem item)
        {
            item.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            HeadlessTestHelpers.Settle();
        }

        string rect = TypeDisplayHelpers.GetLocalizedName(typeof(RectShape));
        // The width and height are a pair that stands apart while only the width has keys, so the width is a row of
        // the size's, as in the graph editor.
        string width = $"{rect}/{PropertyPair.Find(shape.Width)!.DisplayName}/{widthAdapter.DisplayName}";
        string fillRow = $"{rect}/{Property.GetLocalizedName(shape.Fill)}";
        string transformRow = $"{rect}/{Property.GetLocalizedName(shape.Transform)}";
        try
        {
            (FAMenuFlyoutItem showAll, List<(string Path, FAToggleMenuFlyoutItem Item)> toggles) = Open();
            Assert.Multiple(() =>
            {
                Assert.That(showAll.Text, Is.EqualTo(Strings.ShowAllAnimations));
                Assert.That(showAll.IsEnabled, Is.True);
                // The rectangle's submenu holds its own properties, then a submenu for the fill and one for the
                // transform, as the property tab and the graph editor order and nest them.
                Assert.That(toggles.Select(item => item.Path), Is.EqualTo(new[]
                {
                    width,
                    $"{fillRow}/{opacityAdapter.DisplayName}",
                    $"{transformRow}/{pair.DisplayName}",
                }));
                Assert.That(toggles.Select(item => item.Item.IsChecked), Is.All.False);
            });

            Click(toggles[0].Item);
            InlineAnimationLayerViewModel widthLayer = host.Model.Inlines.Single();
            Assert.That(widthLayer.Property.GetEngineProperty(), Is.SameAs(shape.Width));
            // The property tab's menu makes an adapter of its own, which must find the layer already open.
            host.Model.AttachInline(widthAdapter, element);
            Assert.That(host.Model.Inlines, Has.Count.EqualTo(1));

            (showAll, toggles) = Open();
            Assert.That(toggles.Select(item => item.Item.IsChecked), Is.EqualTo(new[] { true, false, false }));
            Click(showAll);
            Assert.Multiple(() =>
            {
                Assert.That(host.Model.Inlines, Has.Count.EqualTo(3));
                Assert.That(host.Model.Inlines.Any(layer => ReferenceEquals(layer.Property, pair)), Is.True,
                    "the position's two properties stand as one layer");
                // An adapter of one of the two, such as a node's port has, finds that layer, as AttachInline opens it.
                var xAdapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)translate.X, translate);
                Assert.That(host.Model.FindInline(element, xAdapter),
                    Is.SameAs(host.Model.Inlines.Single(layer => ReferenceEquals(layer.Property, pair))));
            });

            (showAll, toggles) = Open();
            Assert.Multiple(() =>
            {
                Assert.That(showAll.IsEnabled, Is.False, "every layer is open");
                Assert.That(toggles.Select(item => item.Item.IsChecked), Is.All.True);
            });
            Click(toggles[0].Item);
            Assert.That(host.Model.Inlines.Select(layer => layer.Property.GetEngineProperty()), Has.None.SameAs(shape.Width));
            Assert.That(host.Model.Inlines, Has.Count.EqualTo(2));

            // Separated, the position's row holds a row for each of its properties, whose layers replace the pair's.
            pair.SetKeepsSeparated(true);
            HeadlessTestHelpers.Settle();
            (_, toggles) = Open();
            Assert.Multiple(() =>
            {
                Assert.That(toggles.Select(item => item.Path), Is.EqualTo(new[]
                {
                    width,
                    $"{fillRow}/{opacityAdapter.DisplayName}",
                    $"{transformRow}/{pair.DisplayName}/{pair.Adapters[0].DisplayName}",
                    $"{transformRow}/{pair.DisplayName}/{pair.Adapters[1].DisplayName}",
                }));
                Assert.That(toggles.Select(item => item.Item.IsChecked), Is.EqualTo(new[] { false, true, true, true }));
            });

            // A locked clip lists them, but opens none.
            History(host).ExecuteInTransaction(() => element.IsLocked.Value = true, "lock");
            HeadlessTestHelpers.Settle();
            (showAll, toggles) = Open();
            Assert.Multiple(() =>
            {
                Assert.That(showAll.IsEnabled, Is.False);
                Assert.That(toggles.Select(item => item.Item.IsEnabled), Is.All.False);
            });
        }
        finally
        {
            ((FAMenuFlyout)view.FindControl<Border>("border")!.ContextFlyout!).Hide();
            foreach (InlineAnimationLayerViewModel layer in host.Model.Inlines.ToArray())
                host.Model.DetachInline(layer);
        }
    }

    [AvaloniaTest]
    public async Task A_clip_without_keys_says_so_in_its_animation_menu()
    {
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "animation-marks-none", MediaTime.FromSeconds(10), [Clip(() => new RectShape())]);
        HeadlessTestHelpers.Render(5);
        ElementView view = ViewOf(host, host.Scene.Children.Single());
        var flyout = (FAMenuFlyout)view.FindControl<Border>("border")!.ContextFlyout!;
        flyout.ShowAt(view);
        HeadlessTestHelpers.Render(3);
        try
        {
            var menu = view.FindControl<FAMenuFlyoutSubItem>("animationMenu")!;
            FAMenuFlyoutItem first = menu.Items.OfType<FAMenuFlyoutItem>().First();
            Assert.Multiple(() =>
            {
                Assert.That(first.Text, Is.EqualTo(Strings.NoAnimations));
                Assert.That(first.IsEnabled, Is.False);
                Assert.That(menu.Items.OfType<FAToggleMenuFlyoutItem>(), Is.Empty);
                // The two items the menu had before stay, once each, however often it opens.
                Assert.That(menu.Items.OfType<FAMenuFlyoutItem>().Select(item => item.Text),
                    Is.EqualTo(new[] { Strings.NoAnimations, Strings.FinishEditing, Strings.BringToTop }));
            });
            flyout.Hide();
            flyout.ShowAt(view);
            HeadlessTestHelpers.Render(3);
            Assert.That(menu.Items.OfType<FAMenuFlyoutItem>().Select(item => item.Text),
                Is.EqualTo(new[] { Strings.NoAnimations, Strings.FinishEditing, Strings.BringToTop }));
        }
        finally
        {
            flyout.Hide();
        }
    }

    [Test]
    public void Marks_closer_than_a_diamond_merge_and_keys_on_the_edges_are_drawn_whole()
    {
        float scale = 1;
        double width = MediaTime.FromSeconds(4).TimeToPixel(scale);
        double half = ElementKeyFrameMarks.MarkSize / 2;
        List<double> centers = ElementKeyFrameMarks.Layout(
            Seconds(-1, 0, 0.01, 1, 1.02, 4, 5), scale, width);

        Assert.That(centers, Is.EqualTo(new[] { half, MediaTime.FromSeconds(1).TimeToPixel(scale), width - half }));
    }

    [Test]
    public void A_value_line_holds_before_the_first_key_and_after_the_last_and_follows_the_easing_between()
    {
        KeyFrameAnimation<float> animation = Animation((0.0, 0f), (1.0, 100f));
        var factory = GraphEditorViewViewModelFactory.GetFactory(animation)!;
        InlineEasingGraphControl.ValueLine[] lines = InlineEasingGraphControl.ValueLine.Sample(
            animation, [(0d, animation.KeyFrames[0]), (100d, animation.KeyFrames[1])], -20, 200, factory.ChannelReaders!);

        InlineEasingGraphControl.ValueLine line = lines.Single();
        int middle = Array.IndexOf(line.X, 50d);
        bool hasRange = InlineEasingGraphControl.ValueLine.TryGetRange(lines, out double min, out double max);
        Assert.Multiple(() =>
        {
            Assert.That(line.X[0], Is.EqualTo(-20));
            Assert.That(line.Values[0], Is.Zero);
            Assert.That(middle, Is.GreaterThan(0));
            Assert.That(line.Values[middle], Is.EqualTo(50).Within(0.001));
            Assert.That(line.X[^1], Is.EqualTo(200));
            Assert.That(line.Values[^1], Is.EqualTo(100));
            Assert.That(hasRange, Is.True);
            Assert.That((min, max), Is.EqualTo((0d, 100d)));
            Assert.That(InlineEasingGraphControl.ValueLine.ToHeight(100, min, max), Is.Zero);
            Assert.That(InlineEasingGraphControl.ValueLine.ToHeight(0, min, max), Is.EqualTo(1));
            Assert.That(InlineEasingGraphControl.ValueLine.ToHeight(7, 7, 7), Is.EqualTo(0.5));
        });

        // An easing that overshoots reaches past the keys, and the range takes it in.
        animation.KeyFrames[1].Easing.Value = new BackEaseOut();
        lines = InlineEasingGraphControl.ValueLine.Sample(
            animation, [(0d, animation.KeyFrames[0]), (100d, animation.KeyFrames[1])], 0, 100, factory.ChannelReaders!);
        InlineEasingGraphControl.ValueLine.TryGetRange(lines, out _, out double overshoot);
        Assert.That(overshoot, Is.GreaterThan(100));
    }

    [Test]
    public void A_held_value_jumps_at_the_next_key_rather_than_ramping_to_it()
    {
        KeyFrameAnimation<float> animation = Animation((0.0, 0f), (1.0, 100f));
        animation.KeyFrames[1].Easing.Value = new HoldEasing();
        var factory = GraphEditorViewViewModelFactory.GetFactory(animation)!;
        InlineEasingGraphControl.ValueLine line = InlineEasingGraphControl.ValueLine.Sample(
            animation, [(0d, animation.KeyFrames[0]), (1000d, animation.KeyFrames[1])], 0, 1000,
            factory.ChannelReaders!).Single();

        Assert.Multiple(() =>
        {
            Assert.That(line.Values.Where((_, i) => line.X[i] < 1000), Is.All.Zero, "the value holds up to the key");
            Assert.That(line.X[^2..], Is.EqualTo(new[] { 1000d, 1000d }));
            Assert.That(line.Values[^2..], Is.EqualTo(new[] { 0d, 100d }), "and goes straight up at it");
        });
    }

    [AvaloniaTest]
    public async Task The_animation_menu_lists_the_separated_channels_of_a_brush_that_a_node_port_holds()
    {
        SolidColorBrush? brush = null;
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            "animation-marks-node-port", MediaTime.FromSeconds(10),
            [
                Clip(() =>
                {
                    brush = new SolidColorBrush(Colors.Orange);
                    brush.Color.SetKeyFrameAnimation(Animation((0.0, Colors.Orange), (1.0, Colors.DeepSkyBlue)));
                    ComponentAnimations.Separate(brush.Color);
                    var node = new GeometryShapeNode();
                    node.Fill.Property!.SetValue(brush);
                    var graph = new GraphModel();
                    graph.Nodes.Add(node);
                    var drawable = new NodeGraphDrawable();
                    drawable.Model.Value = graph;
                    return drawable;
                })
            ]);
        HeadlessTestHelpers.Render(5);
        ElementViewModel model = host.Model.FindViewModelFor(host.Scene.Children.Single())!;
        var channels = (IComponentAnimation)brush!.Color.GetPropertyAnimation()!;
        IAnimatablePropertyAdapter[] toggles = [.. model.GetAnimationMenu().SelectMany(row => row.Adapters)];
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(model.AnimationMarks.Value.HasKeyFrames, Is.True);
                Assert.That(toggles.Select(adapter => adapter.GetEngineProperty()), Is.EquivalentTo(channels.Components),
                    "a toggle for each channel of the color, below the node's fill port");
            });
            model.ShowAllInlines(toggles);
            Assert.That(host.Model.Inlines, Has.Count.EqualTo(channels.Components.Count));
        }
        finally
        {
            foreach (InlineAnimationLayerViewModel layer in host.Model.Inlines.ToArray())
                host.Model.DetachInline(layer);
        }
    }

    [Test]
    public void A_value_with_channels_has_a_line_for_each_and_a_value_without_any_has_none()
    {
        var points = new KeyFrameAnimation<GraphicsPoint>();
        points.KeyFrames.Add(new KeyFrame<GraphicsPoint> { Value = { Value = new GraphicsPoint(0, 10) } });
        points.KeyFrames.Add(new KeyFrame<GraphicsPoint>
        {
            KeyTime = { Value = MediaTime.FromSeconds(1) },
            Value = { Value = new GraphicsPoint(100, 10) }
        });
        var factory = GraphEditorViewViewModelFactory.GetFactory(points)!;
        InlineEasingGraphControl.ValueLine[] lines = InlineEasingGraphControl.ValueLine.Sample(
            points, [(0d, points.KeyFrames[0]), (100d, points.KeyFrames[1])], 0, 100, factory.ChannelReaders!);

        Assert.Multiple(() =>
        {
            Assert.That(factory.ChannelNames, Is.EqualTo(new[] { "X", "Y" }));
            Assert.That(lines, Has.Length.EqualTo(2));
            Assert.That(lines[0].Values[^1], Is.EqualTo(100));
            Assert.That(lines[1].Values, Is.All.EqualTo(10));
            Assert.That(GraphEditorViewViewModelFactory.GetFactory(new KeyFrameAnimation<bool>()), Is.Null);
            Assert.That(GraphEditorViewViewModelFactory.GetFactory(new KeyFrameAnimation<Color>())!.ChannelReaders,
                Has.Count.EqualTo(4));
        });
    }

    // Draws clips with marks, a clip too narrow for them, and inline layers of a number, a color and a pair, zoomed in
    // and out, with the Animation menu open. Under BEUTL_HEADLESS_CAPTURE_DIR each step is saved as a PNG for review.
    [AvaloniaTest]
    [TestCase("dark")]
    [TestCase("light")]
    public async Task Clips_and_inline_layers_draw_marks_and_value_lines(string theme)
    {
        RectShape? shape = null;
        RectShape? narrow = null;
        using TimelineTestHost host = await TimelineTestHost.CreateAsync(
            $"animation-marks-capture-{theme}", MediaTime.FromSeconds(10),
            [
                Clip(() =>
                {
                    shape = CreateAnimatedShape();
                    shape.Opacity.Expression = Expression.Create<float>("Sin(Time) * 50 + 50");
                    var color = new KeyFrameAnimation<Color>();
                    color.KeyFrames.Add(new KeyFrame<Color> { Value = { Value = Colors.Orange } });
                    color.KeyFrames.Add(new KeyFrame<Color>
                    {
                        KeyTime = { Value = MediaTime.FromSeconds(2) },
                        Value = { Value = Colors.DeepSkyBlue },
                        Easing = { Value = new BackEaseOut() }
                    });
                    ((SolidColorBrush)shape.Fill.Value!).Color.SetKeyFrameAnimation(color);
                    return shape;
                }),
                Clip(() =>
                {
                    var keys = new RectShape();
                    keys.Height.SetKeyFrameAnimation(Animation((0.0, 0f), (0.02, 1f), (0.04, 2f), (2.0, 3f), (3.9, 4f)));
                    return keys;
                }, startSeconds: 0.5, lengthSeconds: 4, layer: 4),
                // Too narrow for any details, keys included.
                Clip(() =>
                {
                    narrow = new RectShape();
                    narrow.Width.SetKeyFrameAnimation(Animation((0.0, 0f), (0.1, 1f)));
                    return narrow;
                }, startSeconds: 4.2, lengthSeconds: 0.1, layer: 6),
            ],
            width: 1000,
            theme: theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
        Element element = (Element)shape!.HierarchicalParent!;
        ElementViewModel model = host.Model.FindViewModelFor(element)!;
        model.ShowAllInlines(model.GetAnimationMenu().SelectMany(row => row.Adapters));
        HeadlessTestHelpers.Render(8);
        await Task.Delay(500);
        HeadlessTestHelpers.Render(3);
        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(host.View.GetVisualDescendants().OfType<InlineEasingGraphControl>().Count(), Is.EqualTo(4),
                    "the width, the fill's color and opacity, and the position each have a layer");
                Assert.That(host.View.GetVisualDescendants().OfType<ElementView>()
                        .Where(view => view.DataContext is ElementViewModel { Model: var clip }
                                       && clip == narrow!.HierarchicalParent),
                    Is.Empty, "a clip under 25 px is drawn without its details");
            });
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineAnimationMarksTests), $"marks-{theme}");

            host.Window.Height = 760;
            HeadlessTestHelpers.Render(3);
            ElementView view = ViewOf(host, element);
            var flyout = (FAMenuFlyout)view.FindControl<Border>("border")!.ContextFlyout!;
            flyout.ShowAt(view);
            HeadlessTestHelpers.Render(3);
            Assert.That(flyout.IsOpen, Is.True);
            // The Animation menu, the rectangle's submenu in it, and the fill's submenu in that.
            var menu = view.FindControl<FAMenuFlyoutSubItem>("animationMenu")!;
            OpenSubmenu(menu);
            FAMenuFlyoutSubItem rectangle = menu.Items.OfType<FAMenuFlyoutSubItem>().Single();
            OpenSubmenu(rectangle);
            FAMenuFlyoutSubItem fill = rectangle.Items.OfType<FAMenuFlyoutSubItem>()
                .Single(item => item.Text == Property.GetLocalizedName(shape.Fill));
            OpenSubmenu(fill);
            HeadlessCapture.SaveIfRequested(fill.Items.OfType<Control>().First(), nameof(TimelineAnimationMarksTests),
                $"menu-{theme}");
            flyout.Hide();
            host.Window.Height = 420;

            // Zoomed out, keys a few pixels apart show as one diamond.
            host.Model.Options.Value = host.Model.Options.Value with { Scale = 0.25f };
            HeadlessTestHelpers.Render(8);
            await Task.Delay(500);
            HeadlessTestHelpers.Render(3);
            HeadlessCapture.SaveIfRequested(host.Window, nameof(TimelineAnimationMarksTests), $"marks-zoomed-out-{theme}");
        }
        finally
        {
            foreach (InlineAnimationLayerViewModel layer in host.Model.Inlines.ToArray())
                host.Model.DetachInline(layer);
        }

        static void OpenSubmenu(FAMenuFlyoutSubItem submenu)
        {
            Assert.That(submenu.Focus(), Is.True, $"{submenu.Text} can take the focus");
            TopLevel popup = TopLevel.GetTopLevel(submenu)!;
            popup.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            popup.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            HeadlessTestHelpers.Render(3);
            Assert.That(submenu.Items.OfType<Control>().First().Bounds.Height, Is.GreaterThan(0), $"{submenu.Text} opened");
        }
    }
}
