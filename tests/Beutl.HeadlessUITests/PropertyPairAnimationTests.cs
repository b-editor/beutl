using System.Reactive.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.GraphEditorTab.Views;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Beutl.HeadlessUITests;

// The keyframes of two properties that stand as one: one animation of their one value, which is only a view of the two.
[TestFixture]
public sealed class PropertyPairAnimationTests
{
    private static KeyFrameAnimation<float> Keys(params (double Seconds, float Value)[] keys)
    {
        var animation = new KeyFrameAnimation<float>();
        foreach ((double seconds, float value) in keys)
        {
            animation.KeyFrames.Add(new KeyFrame<float>
            {
                KeyTime = { Value = MediaTime.FromSeconds(seconds) },
                Value = { Value = value },
                Easing = { Value = new SplineEasing(0.25f, 0.1f, 0.25f, 1f) }
            }, out _);
        }

        return animation;
    }

    private static async Task<(EditViewModel Editor, RectShape Shape, Element Element)> CreateAsync(bool animated = true)
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"property-pair-{Guid.NewGuid():N}");
        var shape = new RectShape();
        if (animated)
        {
            shape.Width.SetKeyFrameAnimation(Keys((0, 100), (2, 200)));
            shape.Height.SetKeyFrameAnimation(Keys((0, 50), (2, 150)));
        }

        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(4));
        return (editor, shape, editor.Scene.Children.Single());
    }

    private static KeyFrameAnimation<Size> Joined(RectShape shape)
        => (KeyFrameAnimation<Size>)PropertyPair.Find(shape.Width)!.Animation!;

    private static double[] Times(IKeyFrameAnimation animation)
        => [.. animation.KeyFrames.Select(key => key.KeyTime.Value.ToSeconds())];

    private static IEnumerable<GraphEditorTreeItemViewModel> Flatten(IEnumerable<GraphEditorTreeItemViewModel> items)
        => items.SelectMany(item => Flatten(item.Children).Prepend(item));

    private static T PrivateField<T>(object target, string name)
        => (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;

    private static GraphEditorTreeItemViewModel Row(GraphEditorTabViewModel tab, RectShape shape)
        => Flatten(tab.TreeItems).Single(item => ReferenceEquals(item.Adapter, PropertyPair.Find(shape.Width)));

    [AvaloniaTest]
    public async Task The_joined_keyframes_show_both_values()
    {
        (_, RectShape shape, _) = await CreateAsync();

        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        KeyFrameAnimation<Size> joined = Joined(shape);
        Assert.Multiple(() =>
        {
            Assert.That(PropertyPair.Find(shape.Height), Is.SameAs(pair));
            Assert.That(PropertyPair.Find(shape.Opacity), Is.Null);
            Assert.That(pair.DisplayName, Is.EqualTo(Strings.Size));
            Assert.That(Times(joined), Is.EqualTo(new[] { 0d, 2d }));
            Assert.That(joined.KeyFrames.Select(key => key.Value), Is.EqualTo(new object[] { new Size(100, 50), new Size(200, 150) }));
            Assert.That(joined.Id.Value, Is.EqualTo(shape.Width.GetKeyFrameAnimation()!.Id.Value));
            Assert.That(((IHierarchical)shape).HierarchicalChildren, Does.Not.Contain(joined), "It is not part of the object.");
        });
    }

    [AvaloniaTest]
    public async Task Moving_a_joined_key_moves_both_as_one_history_entry_without_splitting_on_undo()
    {
        (EditViewModel editor, RectShape shape, _) = await CreateAsync();
        KeyFrameAnimation<Size> joined = Joined(shape);
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        HistoryManager history = editor.HistoryManager;
        history.Commit();
        int before = history.UndoCount;
        var separations = new List<bool>();
        using IDisposable subscription = pair.IsSeparated.Skip(1).Subscribe(separations.Add);

        joined.KeyFrames[1].KeyTime.Value = MediaTime.FromSeconds(1);
        history.Commit();

        Assert.That(Times(shape.Width.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 1d }));
        Assert.That(Times(shape.Height.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 1d }));
        Assert.That(history.UndoCount, Is.EqualTo(before + 1));

        history.Undo();
        HeadlessTestHelpers.Settle();

        Assert.Multiple(() =>
        {
            Assert.That(Times(shape.Width.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 2d }));
            Assert.That(Times(shape.Height.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 2d }));
            Assert.That(Times(joined), Is.EqualTo(new[] { 0d, 2d }));
            Assert.That(pair.Animation, Is.SameAs(joined));
            Assert.That(separations, Is.Empty, "The two pass through differing only while undo runs.");
        });
    }

    [AvaloniaTest]
    public async Task Adding_and_removing_a_joined_key_reaches_both()
    {
        (EditViewModel editor, RectShape shape, _) = await CreateAsync();
        KeyFrameAnimation<Size> joined = Joined(shape);
        KeyFrameAnimation<float> width = shape.Width.GetKeyFrameAnimation()!;
        KeyFrameAnimation<float> height = shape.Height.GetKeyFrameAnimation()!;
        HistoryManager history = editor.HistoryManager;
        history.Commit();
        float expectedWidth = width.Interpolate(MediaTime.FromSeconds(1));
        float expectedHeight = height.Interpolate(MediaTime.FromSeconds(1));

        AnimationOperations.InsertKeyFrame(joined, null, MediaTime.FromSeconds(1), NullLogger.Instance);
        history.Commit();

        Assert.Multiple(() =>
        {
            Assert.That(Times(width), Is.EqualTo(new[] { 0d, 1d, 2d }));
            Assert.That(Times(height), Is.EqualTo(new[] { 0d, 1d, 2d }));
            Assert.That(((KeyFrame<float>)width.KeyFrames[1]).Value.Value, Is.EqualTo(expectedWidth).Within(0.001));
            Assert.That(((KeyFrame<float>)height.KeyFrames[1]).Value.Value, Is.EqualTo(expectedHeight).Within(0.001));
            Assert.That(width.KeyFrames[1].Easing.Value, Is.Not.SameAs(height.KeyFrames[1].Easing.Value), "Each key has an easing of its own.");
            Assert.That(PropertyPair.Find(shape.Width)!.IsSeparated.Value, Is.False);
        });

        IKeyFrame key = joined.KeyFrames[1];
        AnimationOperations.RemoveKeyFrame(joined, key, NullLogger.Instance);
        history.Commit();
        Assert.That(Times(width), Is.EqualTo(new[] { 0d, 2d }));
        Assert.That(Times(height), Is.EqualTo(new[] { 0d, 2d }));

        history.Undo();
        HeadlessTestHelpers.Settle();
        Assert.That(Times(width), Is.EqualTo(new[] { 0d, 1d, 2d }));
        Assert.That(joined.KeyFrames[1], Is.SameAs(key), "What the graph editor's selection holds comes back with the key.");
    }

    [AvaloniaTest]
    public async Task A_joined_value_follows_the_range_of_each_property()
    {
        (_, RectShape shape, _) = await CreateAsync();
        KeyFrameAnimation<Size> joined = Joined(shape);

        joined.KeyFrames[1].Value = new Size(-5, 7);

        Assert.Multiple(() =>
        {
            Assert.That(((KeyFrame<float>)shape.Width.GetKeyFrameAnimation()!.KeyFrames[1]).Value.Value, Is.Zero);
            Assert.That(((KeyFrame<float>)shape.Height.GetKeyFrameAnimation()!.KeyFrames[1]).Value.Value, Is.EqualTo(7f));
            Assert.That(joined.KeyFrames[1].Value, Is.EqualTo(new Size(0, 7)));
        });
    }

    [AvaloniaTest]
    public async Task An_edit_of_either_property_comes_back_to_the_joined_keyframes()
    {
        (_, RectShape shape, _) = await CreateAsync();
        KeyFrameAnimation<Size> joined = Joined(shape);

        ((KeyFrame<float>)shape.Height.GetKeyFrameAnimation()!.KeyFrames[0]).Value.Value = 75;

        Assert.That(joined.KeyFrames[0].Value, Is.EqualTo(new Size(100, 75)));
    }

    [AvaloniaTest]
    public async Task Easings_reach_both_without_sharing_an_object()
    {
        (EditViewModel editor, RectShape shape, _) = await CreateAsync();
        KeyFrameAnimation<Size> joined = Joined(shape);
        IKeyFrame width = shape.Width.GetKeyFrameAnimation()!.KeyFrames[1];
        IKeyFrame height = shape.Height.GetKeyFrameAnimation()!.KeyFrames[1];
        HistoryManager history = editor.HistoryManager;
        history.Commit();

        // The graph editor moves a control point of the easing that it has.
        ((SplineEasing)joined.KeyFrames[1].Easing.Value).X1 = 0.5f;
        history.Commit();
        Assert.That(((SplineEasing)width.Easing.Value).X1, Is.EqualTo(0.5f));
        Assert.That(((SplineEasing)height.Easing.Value).X1, Is.EqualTo(0.5f));

        history.Undo();
        HeadlessTestHelpers.Settle();
        Assert.That(((SplineEasing)width.Easing.Value).X1, Is.EqualTo(0.25f));
        Assert.That(((SplineEasing)height.Easing.Value).X1, Is.EqualTo(0.25f));

        joined.KeyFrames[1].Easing.Value = new SplineEasing(0.1f, 0.2f, 0.3f, 0.4f);
        Assert.Multiple(() =>
        {
            Assert.That(height.Easing.Value, Is.Not.SameAs(width.Easing.Value));
            Assert.That(((SplineEasing)height.Easing.Value).Y2, Is.EqualTo(0.4f));
            Assert.That(PropertyPair.Find(shape.Width)!.IsSeparated.Value, Is.False);
        });

        joined.KeyFrames[1].Easing.Value = new LinearEasing();
        Assert.That(width.Easing.Value, Is.TypeOf<LinearEasing>());
        Assert.That(height.Easing.Value, Is.TypeOf<LinearEasing>());
    }

    [AvaloniaTest]
    public async Task Keys_with_other_easings_stand_apart()
    {
        (_, RectShape shape, _) = await CreateAsync();
        PropertyPair pair = PropertyPair.Find(shape.Width)!;

        IKeyFrame key = shape.Height.GetKeyFrameAnimation()!.KeyFrames[1];
        key.Easing.Value = new SplineEasing(0.9f, 0.9f, 0.9f, 0.9f);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(pair.IsSeparated.Value, Is.True);
            Assert.That(pair.CanStandForBoth.Value, Is.False);
            Assert.That(pair.Animation, Is.Null);
        });

        key.Easing.Value = new SplineEasing(0.25f, 0.1f, 0.25f, 1f);
        HeadlessTestHelpers.Settle();
        Assert.That(pair.IsSeparated.Value, Is.False);
        Assert.That(pair.Animation, Is.Not.Null);
    }

    // Moving a control point edits neither its key nor the property, so only the easing can tell.
    [AvaloniaTest]
    public async Task Moving_a_curve_until_the_two_match_joins_them_again()
    {
        (_, RectShape shape, _) = await CreateAsync();
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        var curve = (SplineEasing)shape.Height.GetKeyFrameAnimation()!.KeyFrames[0].Easing.Value;
        float x1 = curve.X1;

        curve.X1 = x1 / 2;
        HeadlessTestHelpers.Settle();
        Assert.That(pair.IsSeparated.Value, Is.True);
        Assert.That(pair.CanStandForBoth.Value, Is.False);

        curve.X1 = x1;
        HeadlessTestHelpers.Settle();
        Assert.That(pair.IsSeparated.Value, Is.False);
        Assert.That(pair.CanStandForBoth.Value, Is.True);
        Assert.That(pair.Animation, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task A_curve_moved_while_the_two_stand_apart_stays_its_own()
    {
        (_, RectShape shape, _) = await CreateAsync();
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        pair.SetKeepsSeparated(true);
        var first = (SplineEasing)shape.Width.GetKeyFrameAnimation()!.KeyFrames[0].Easing.Value;
        var second = (SplineEasing)shape.Height.GetKeyFrameAnimation()!.KeyFrames[0].Easing.Value;
        float x1 = second.X1;

        first.X1 = x1 / 2;
        HeadlessTestHelpers.Settle();

        Assert.That(second.X1, Is.EqualTo(x1));
        Assert.That(pair.CanStandForBoth.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task The_joined_keyframes_have_the_time_origin_of_the_properties()
    {
        (_, RectShape shape, Element element) = await CreateAsync();
        KeyFrameAnimation<Size> joined = Joined(shape);

        element.Start.Value = MediaTime.FromSeconds(3);

        Assert.That(AnimationOperations.GetTimeOrigin(joined), Is.EqualTo(AnimationOperations.GetTimeOrigin(shape.Width.GetKeyFrameAnimation()!)));
        Assert.That(AnimationOperations.GetTimeOrigin(joined), Is.EqualTo(MediaTime.FromSeconds(3)));
    }

    [AvaloniaTest]
    public async Task A_layer_moves_both_and_deleting_its_animation_removes_both()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;

        // Whichever property it is asked for, as restoring a layer does.
        timeline.AttachInline(new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Height, shape), element);

        InlineAnimationLayerViewModel inline = timeline.Inlines.Single();
        Assert.That(inline.Property, Is.SameAs(PropertyPair.Find(shape.Width)));
        Assert.That(inline.Header, Is.EqualTo(Strings.Size));
        Assert.That(inline.Items, Has.Count.EqualTo(2));

        InlineKeyFrameViewModel key = inline.Items[1];
        key.Left.Value = MediaTime.FromSeconds(1).TimeToPixel(timeline.Options.Value.Scale);
        key.UpdateKeyTime();
        Assert.That(Times(shape.Width.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 1d }));
        Assert.That(Times(shape.Height.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 1d }));

        inline.DeleteAnimation();
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(shape.Width.GetKeyFrameAnimation(), Is.Null);
            Assert.That(shape.Height.GetKeyFrameAnimation(), Is.Null);
            Assert.That(timeline.Inlines, Is.Empty);
        });
    }

    [AvaloniaTest]
    public async Task Layers_follow_whether_the_two_stand_as_one()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        timeline.AttachInline(pair, element);
        IProperty[] both = [shape.Width, shape.Height];

        pair.SetKeepsSeparated(true);
        HeadlessTestHelpers.Settle();
        Assert.That(timeline.Inlines.Select(inline => inline.Property.GetEngineProperty()), Is.EqualTo(both));

        pair.SetKeepsSeparated(false);
        HeadlessTestHelpers.Settle();
        Assert.That(timeline.Inlines.Single().Property, Is.SameAs(pair));

        IKeyFrame key = shape.Height.GetKeyFrameAnimation()!.KeyFrames[1];
        key.KeyTime.Value = MediaTime.FromSeconds(3);
        HeadlessTestHelpers.Settle();
        Assert.That(timeline.Inlines.Select(inline => inline.Property.GetEngineProperty()), Is.EqualTo(both));

        key.KeyTime.Value = MediaTime.FromSeconds(2);
        HeadlessTestHelpers.Settle();
        Assert.That(timeline.Inlines.Single().Property, Is.SameAs(pair));

        timeline.DetachInline(timeline.Inlines.Single());
        pair.SetKeepsSeparated(true);
        HeadlessTestHelpers.Settle();
        Assert.That(timeline.Inlines, Is.Empty, "A layer the user closed stays closed.");
    }

    [AvaloniaTest]
    public async Task Separating_shows_a_layer_only_for_what_is_animated()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        timeline.AttachInline(PropertyPair.Find(shape.Width)!, element);

        shape.Height.SetKeyFrameAnimation(null);
        HeadlessTestHelpers.Settle();

        Assert.That(timeline.Inlines.Single().Property.GetEngineProperty(), Is.SameAs(shape.Width));
    }

    [AvaloniaTest]
    public async Task A_layer_is_restored_as_one()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        timeline.AttachInline(pair, element);
        var json = new JsonObject();
        timeline.WriteToJson(json);
        timeline.DetachInline(timeline.Inlines.Single());

        timeline.ReadFromJson(json);

        Assert.That(timeline.Inlines.Single().Property, Is.SameAs(pair));
    }

    [AvaloniaTest]
    public async Task The_timeline_lets_go_of_the_two_when_their_last_layer_closes()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        var listened = PrivateField<Dictionary<PropertyPair, IDisposable>>(timeline, "_inlinePairs");
        timeline.AttachInline(pair, element);
        pair.SetKeepsSeparated(true);
        HeadlessTestHelpers.Settle();

        timeline.DetachInline(timeline.Inlines[0]);
        Assert.That(listened, Does.ContainKey(pair), "One layer is still open.");

        timeline.DetachInline(timeline.Inlines.Single());
        Assert.That(listened, Is.Empty);

        timeline.AttachInline(pair, element);
        pair.SetKeepsSeparated(false);
        HeadlessTestHelpers.Settle();
        Assert.That(timeline.Inlines.Single().Property, Is.SameAs(pair), "A layer opened again follows the two again.");
    }

    [AvaloniaTest]
    public async Task The_graph_editor_has_one_row_with_a_channel_for_each_value()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        using var tab = new GraphEditorTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();

        GraphEditorTreeItemViewModel row = Row(tab, shape);
        KeyFrameAnimation<Size> joined = Joined(shape);
        Assert.Multiple(() =>
        {
            Assert.That(row.Name.Value, Is.EqualTo(Strings.Size));
            Assert.That(row.Children.Select(child => child.Name.Value), Is.EqualTo(new[] { Strings.Width, Strings.Height }));
            Assert.That(row.HasSeparatedComponents.Value, Is.False);
            Assert.That(row.CanSeparateComponents.Value, Is.True);
            Assert.That(Flatten(tab.TreeItems).Where(item => item.Property == shape.Width || item.Property == shape.Height), Is.Empty);
        });

        tab.Select(joined);
        HeadlessTestHelpers.Settle();
        Assert.That(tab.SelectedTreeItem.Value, Is.SameAs(row));
        GraphEditorViewModel graph = tab.SelectedAnimation.Value!;
        Assert.That(graph.Animation, Is.SameAs(joined));
        Assert.That(graph.Views.Select(view => view.Name), Is.EqualTo(new[] { "Width", "Height" }));
    }

    [AvaloniaTest]
    public async Task The_row_turns_animation_on_and_off_for_both()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync(animated: false);
        shape.Width.Value = 30;
        shape.Height.Value = 40;
        using var tab = new GraphEditorTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        GraphEditorTreeItemViewModel row = Row(tab, shape);
        editor.HistoryManager.Commit();
        int before = editor.HistoryManager.UndoCount;

        tab.EnableAnimation(row);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(((KeyFrame<float>)shape.Width.GetKeyFrameAnimation()!.KeyFrames.Single()).Value.Value, Is.EqualTo(30f));
            Assert.That(((KeyFrame<float>)shape.Height.GetKeyFrameAnimation()!.KeyFrames.Single()).Value.Value, Is.EqualTo(40f));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
            Assert.That(row.Animation, Is.SameAs(Joined(shape)));
        });

        tab.RemoveAnimation(row);
        HeadlessTestHelpers.Settle();
        Assert.That(shape.Width.GetKeyFrameAnimation(), Is.Null);
        Assert.That(shape.Height.GetKeyFrameAnimation(), Is.Null);
        Assert.That(row.Animation, Is.Null);
    }

    [AvaloniaTest]
    public async Task The_row_toggles_the_keyframe_of_both_at_the_playhead()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        using var tab = new GraphEditorTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        GraphEditorTreeItemViewModel row = Row(tab, shape);
        ((IEditorClock)editor.GetService(typeof(IEditorClock))!).Seek(MediaTime.FromSeconds(1));
        HeadlessTestHelpers.Settle();

        tab.ToggleKeyFrame(row);
        HeadlessTestHelpers.Settle();
        Assert.That(Times(shape.Width.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 1d, 2d }));
        Assert.That(Times(shape.Height.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 1d, 2d }));

        tab.ToggleKeyFrame(row);
        HeadlessTestHelpers.Settle();
        Assert.That(Times(shape.Width.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 2d }));
        Assert.That(Times(shape.Height.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 2d }));
        Assert.That(PropertyPair.Find(shape.Width)!.IsSeparated.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task Deleting_selected_keys_removes_them_from_both_and_undo_restores_the_selection()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        using var tab = new GraphEditorTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        tab.Select(Joined(shape));
        HeadlessTestHelpers.Settle();
        GraphEditorViewModel graph = tab.SelectedAnimation.Value!;
        IKeyFrame key = graph.Animation.KeyFrames[1];
        graph.SelectedView.Value!.SetSelection([key]);

        Assert.That(graph.DeleteKeyFrames([key]), Is.True);
        Assert.That(Times(shape.Width.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d }));
        Assert.That(Times(shape.Height.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d }));

        editor.HistoryManager.Undo();
        HeadlessTestHelpers.Settle();
        Assert.That(Times(shape.Width.GetKeyFrameAnimation()!), Is.EqualTo(new[] { 0d, 2d }));
        Assert.That(graph.Animation.KeyFrames[1], Is.SameAs(key));
        Assert.That(graph.SelectedView.Value!.IsKeyFrameSelected(key), Is.True);
    }

    [AvaloniaTest]
    public async Task The_selection_stays_with_the_two_when_they_part_and_join()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        using var tab = new GraphEditorTabViewModel(editor);
        tab.Element.Value = element;
        // The tree clears its selection as soon as the selected row leaves it, which only a shown tree does.
        var window = new Window { Content = new GraphEditorTabView { DataContext = tab } };
        window.Show();
        HeadlessTestHelpers.Settle();
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        tab.Select(pair.Animation);
        Row(tab, shape).IsExpanded.Value = true;
        HeadlessTestHelpers.Settle();

        pair.SetKeepsSeparated(true);
        HeadlessTestHelpers.Settle();
        Assert.That(tab.SelectedTreeItem.Value!.Property, Is.SameAs(shape.Width));
        Assert.That(tab.SelectedAnimation.Value!.Animation, Is.SameAs(shape.Width.GetKeyFrameAnimation()));

        pair.SetKeepsSeparated(false);
        HeadlessTestHelpers.Settle();
        Assert.That(tab.SelectedTreeItem.Value, Is.SameAs(Row(tab, shape)));
        Assert.That(tab.SelectedAnimation.Value!.Animation, Is.SameAs(pair.Animation));
        window.Close();
    }

    [AvaloniaTest]
    public async Task The_graph_editor_lets_go_of_the_two_when_they_leave_its_tree()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        using var tab = new GraphEditorTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        var listened = PrivateField<HashSet<PropertyPair>>(tab, "_observedPairs");
        Assert.That(listened, Does.Contain(pair));

        tab.Element.Value = null;
        HeadlessTestHelpers.Settle();
        Assert.That(listened, Is.Empty);

        pair.SetKeepsSeparated(true);
        Assert.That(PrivateField<bool>(tab, "_refreshPending"), Is.False);
    }

    [AvaloniaTest]
    public async Task The_tree_shows_a_row_for_each_while_the_two_stand_apart()
    {
        (EditViewModel editor, RectShape shape, Element element) = await CreateAsync();
        using var tab = new GraphEditorTabViewModel(editor);
        tab.Element.Value = element;
        HeadlessTestHelpers.Settle();
        GraphEditorTreeItemViewModel row = Row(tab, shape);
        PropertyPair pair = PropertyPair.Find(shape.Width)!;
        int before = editor.HistoryManager.UndoCount;

        tab.ToggleComponentSeparation(row);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(pair.KeepsSeparated.Value, Is.True);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before), "Only the view changes.");
            Assert.That(row.HasSeparatedComponents.Value, Is.True);
            Assert.That(row.Animation, Is.Null);
            Assert.That(row.Children.Select(child => child.Property), Is.EqualTo(new IProperty[] { shape.Width, shape.Height }));
            Assert.That(row.Children.Select(child => child.Animation),
                Is.EqualTo(new IKeyFrameAnimation[] { shape.Width.GetKeyFrameAnimation()!, shape.Height.GetKeyFrameAnimation()! }));
        });

        tab.ToggleComponentSeparation(row);
        HeadlessTestHelpers.Settle();
        Assert.That(pair.KeepsSeparated.Value, Is.False);
        Assert.That(row.HasSeparatedComponents.Value, Is.False);
        Assert.That(row.Animation, Is.SameAs(Joined(shape)));

        shape.Height.SetKeyFrameAnimation(null);
        HeadlessTestHelpers.Settle();
        Assert.Multiple(() =>
        {
            Assert.That(row.HasSeparatedComponents.Value, Is.True);
            Assert.That(row.CanSeparateComponents.Value, Is.False, "They cannot rejoin while they differ.");
            Assert.That(row.Children.Select(child => child.Animation != null), Is.EqualTo(new[] { true, false }));
        });
    }
}
