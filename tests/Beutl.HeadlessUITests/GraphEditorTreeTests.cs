using System.Text.Json.Nodes;
using Avalonia;
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
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.Testing.Headless;
using Beutl.Validation;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using DrawableGroup = Beutl.Graphics.DrawableGroup;
using FluentIcon = FluentIcons.Avalonia.Fluent.FluentIcon;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class GraphEditorTreeTests
{
    [AvaloniaTest]
    [TestCase(640, false)]
    [TestCase(640, true)]
    [TestCase(960, false)]
    [TestCase(960, true)]
    public async Task Property_tree_shows_unanimated_nested_properties_and_timeline_height_rows(int width, bool light)
    {
        using var scope = await TreeScope.CreateAsync(width, light);
        var root = scope.Model.TreeItems.Single();
        Assert.That(root.Name.Value, Is.EqualTo(TypeDisplayHelpers.GetLocalizedName(typeof(EllipseShape))));
        var widthItem = scope.Find(scope.Shape.Width);
        var heightItem = scope.Find(scope.Shape.Height);
        var fill = scope.Find(scope.Shape.Fill);
        var color = scope.Find(scope.Brush.Color);
        Assert.That(widthItem.Parent, Is.SameAs(root));
        Assert.That(heightItem.Parent, Is.SameAs(root));
        Assert.That(fill.Parent, Is.SameAs(root));
        Assert.That(color.Parent, Is.SameAs(fill));
        Assert.That(color.Children.Select(x => x.ChannelName), Is.EqualTo(new[] { "Red", "Green", "Blue", "Alpha" }));
        Assert.That(color.Children.Select(x => x.Name.Value), Is.EqualTo(new[] { Strings.Red, Strings.Green, Strings.Blue, Strings.GraphAlpha }));
        Assert.That(widthItem.CanAnimate.Value, Is.True);
        Assert.That(widthItem.HasAnimation.Value, Is.False);
        Assert.That(fill.CanAnimate.Value, Is.False);
        Assert.That(scope.Model.Items, Is.Empty);
        fill.IsExpanded.Value = true;
        color.IsExpanded.Value = true;
        HeadlessTestHelpers.Render(3);
        var tree = scope.View.FindControl<TreeView>("PropertiesTree")!;
        var headers = tree.GetVisualDescendants().OfType<Border>()
            .Where(x => x.Name == "PART_LayoutRoot" && x.IsEffectivelyVisible).ToArray();
        Assert.That(headers.Length, Is.GreaterThan(8));
        Assert.That(scope.View.TryFindResource("LayerHeight", out object? height), Is.True);
        foreach (var header in headers)
            Assert.That(header.Bounds.Height, Is.EqualTo((double)height!).Within(0.01));
        var tops = headers.Select(x => x.TranslatePoint(default, tree)!.Value.Y).Order().ToArray();
        for (int i = 1; i < tops.Length; i++)
            Assert.That(tops[i] - tops[i - 1], Is.EqualTo((double)height!).Within(0.01),
                "Margins between tree headers must not make rows taller than the timeline headers.");
        var labels = tree.GetVisualDescendants().OfType<Avalonia.Controls.TextBlock>()
            .Where(x => x.Name == "PropertyNameText" && x.IsEffectivelyVisible)
            .GroupBy(x => ((GraphEditorTreeItemViewModel)x.DataContext!).Parent);
        foreach (var siblings in labels)
        {
            var positions = siblings.Select(x => x.TranslatePoint(default, tree)!.Value.X).ToArray();
            Assert.That(positions.Max() - positions.Min(), Is.LessThan(0.01),
                "Sibling labels must align whether or not they have an expand/collapse arrow.");
        }
        scope.Capture($"property-tree-{width}-{light}");
        var previous = color.Children.ToArray();
        scope.Model.Refresh();
        Assert.That(scope.Find(scope.Brush.Color), Is.SameAs(color));
        Assert.That(color.Children, Is.EqualTo(previous));
        Assert.That(fill.IsExpanded.Value, Is.True);
        Assert.That(color.IsExpanded.Value, Is.True);
    }

    [AvaloniaTest]
    public async Task Unsupported_property_types_do_not_offer_animation_controls()
    {
        using var scope = await TreeScope.CreateAsync();
        foreach (IProperty property in new IProperty[] { scope.Shape.BlendMode, scope.Shape.AlignmentX, scope.Shape.AlignmentY })
        {
            var item = scope.Find(property);
            Assert.That(item.CanAnimate.Value, Is.False, property.Name);
            Assert.That(scope.AnimationButton(item).IsVisible, Is.False, property.Name);
            scope.Model.EnableAnimation(item);
            Assert.That(property.Animation, Is.Null, property.Name);
        }
        scope.Capture("property-tree-supported-animation-controls");
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Existing_unsupported_animation_can_be_removed_from_the_tree_and_restored(bool light)
    {
        using var scope = await TreeScope.CreateAsync(light: light);
        var animation = new KeyFrameAnimation<AlignmentX>();
        animation.KeyFrames.Add(new KeyFrame<AlignmentX> { Value = AlignmentX.Left });
        scope.Shape.AlignmentX.Animation = animation;
        scope.Base.Model.HistoryManager.Commit();
        HeadlessTestHelpers.Render(3);
        var item = scope.Find(scope.Shape.AlignmentX);
        var button = scope.AnimationButton(item);
        Assert.That(item.CanAnimate.Value, Is.False);
        Assert.That(button.IsVisible, Is.True);
        Assert.That(ToolTip.GetTip(button), Is.EqualTo(Strings.RemoveAnimation));
        int undo = scope.Base.Model.HistoryManager.UndoCount;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render();
        var menu = (FAMenuFlyout)button.ContextFlyout!;
        Assert.That(menu.IsOpen, Is.True);
        Assert.That(animation.KeyFrames, Has.Count.EqualTo(1));
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        scope.Capture($"unsupported-animation-menu-{light}");
        var remove = menu.Items.OfType<FAMenuFlyoutItem>().Single();
        Assert.That(remove.IsEnabled, Is.True);
        if (Environment.GetEnvironmentVariable("BEUTL_GRAPH_CONTEXT_CAPTURE") is { Length: > 0 } directory)
        {
            using var frame = TopLevel.GetTopLevel(remove)?.CaptureRenderedFrame();
            frame?.Save(Path.Combine(directory, $"unsupported-animation-popup-{light}.png"), PngBitmapEncoderOptions.Default);
        }
        remove.Focus();
        scope.Base.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        scope.Base.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.AlignmentX.Animation, Is.Null);
        Assert.That(button.IsVisible, Is.False);
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        scope.Base.Model.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.AlignmentX.Animation, Is.SameAs(animation));
        Assert.That(button.IsVisible, Is.True);
        Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), scope.Base.Window)!.Value;
        scope.Base.RightClick(point);
        Assert.That(menu.IsOpen, Is.True);
        menu.Hide();
        scope.Base.Model.HistoryManager.Redo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.AlignmentX.Animation, Is.Null);
        Assert.That(button.IsVisible, Is.False);
        scope.Model.EnableAnimation(item);
        Assert.That(scope.Shape.AlignmentX.Animation, Is.Null);
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Existing_custom_animation_is_preserved_until_explicit_removal(bool refreshFirst, bool light)
    {
        using var scope = await TreeScope.CreateAsync(light: light);
        var item = scope.Find(scope.Shape.Width);
        var provider = new ConstantAnimation();
        scope.Shape.Width.Animation = provider;
        scope.Base.Model.HistoryManager.Commit();
        if (refreshFirst) HeadlessTestHelpers.Render(3);
        int undo = scope.Base.Model.HistoryManager.UndoCount;
        scope.Model.EnableAnimation(item);
        scope.Model.ToggleKeyFrame(item);
        Assert.That(scope.Shape.Width.Animation, Is.SameAs(provider));
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo));
        HeadlessTestHelpers.Render(3);
        Assert.That(item.CanAnimate.Value, Is.False);
        Assert.That(item.HasAnimation.Value, Is.True);
        var button = scope.AnimationButton(item);
        Assert.That(button.IsVisible, Is.True);
        Assert.That(ToolTip.GetTip(button), Is.EqualTo(Strings.RemoveAnimation));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        var menu = (FAMenuFlyout)button.ContextFlyout!;
        Assert.That(menu.IsOpen, Is.True);
        Assert.That(scope.Shape.Width.Animation, Is.SameAs(provider));
        scope.Capture($"custom-animation-removal-{refreshFirst}-{light}");
        var remove = menu.Items.OfType<FAMenuFlyoutItem>().Single();
        remove.Focus();
        scope.Base.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        scope.Base.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.Width.Animation, Is.Null);
        Assert.That(item.CanAnimate.Value, Is.True);
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        scope.Base.Model.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.Width.Animation, Is.SameAs(provider));
        Assert.That(item.CanAnimate.Value, Is.False);
        scope.Base.Model.HistoryManager.Redo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.Width.Animation, Is.Null);
    }

    public sealed class ConstantAnimation : Hierarchical, IAnimation<float>
    {
        public TimeSpan Duration => TimeSpan.FromSeconds(5);
        public bool UseGlobalClock => false;
        public Type ValueType => typeof(float);
        public IValidator<float>? Validator { get; set; }
        public event EventHandler? Edited { add { } remove { } }
        public float GetAnimatedValue(TimeSpan time) => 84;
        public float Interpolate(TimeSpan timeSpan) => 84;
    }

    [AvaloniaTest]
    public async Task Animation_button_preserves_the_value_and_undo_keeps_the_property_visible()
    {
        using var scope = await TreeScope.CreateAsync();
        var item = scope.Find(scope.Shape.Width);
        int undo = scope.Base.Model.HistoryManager.UndoCount;
        scope.AnimationButton(item).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        var animation = (KeyFrameAnimation<float>)scope.Shape.Width.Animation!;
        Assert.That(animation.KeyFrames, Has.Count.EqualTo(1));
        Assert.That(animation.KeyFrames[0].Value, Is.EqualTo(240));
        Assert.That(animation.KeyFrames[0].KeyTime, Is.EqualTo(TimeSpan.Zero));
        Assert.That(scope.Model.SelectedAnimation.Value!.Animation, Is.SameAs(animation));
        Assert.That(scope.Model.SelectedTreeItem.Value, Is.SameAs(item));
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        scope.Model.EnableAnimation(item);
        Assert.That(scope.Shape.Width.Animation, Is.SameAs(animation));
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        scope.Base.Model.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.Width.Animation, Is.Null);
        Assert.That(scope.Find(scope.Shape.Width), Is.SameAs(item));
        Assert.That(item.HasAnimation.Value, Is.False);
        Assert.That(scope.Model.SelectedAnimation.Value, Is.Null);
        Assert.That(scope.Model.TreeItems, Is.Not.Empty);
        scope.Base.Model.HistoryManager.Redo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Model.SelectedAnimation.Value!.Animation, Is.SameAs(animation));
        Assert.That(item.HasAnimation.Value, Is.True);
        scope.Model.EnableAnimation(scope.Find(scope.Shape.Fill));
        Assert.That(scope.Shape.Fill.Animation, Is.Null, "An object-valued non-animatable property must remain unchanged.");
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task Animation_button_toggles_keys_at_the_playhead_and_tracks_its_icon(bool globalClock, bool light)
    {
        using var scope = await TreeScope.CreateAsync(light: light);
        var item = scope.Find(scope.Shape.Width);
        var button = scope.AnimationButton(item);
        var icon = button.GetVisualDescendants().OfType<FluentIcon>().Single();
        Assert.That(ToolTip.GetTip(button), Is.EqualTo(Strings.EnableAnimation));
        Assert.That(icon.Icon, Is.EqualTo(Icon.Timer));
        scope.Base.Model.Element!.Start = TimeSpan.FromSeconds(2);
        double origin = globalClock ? 2 : 0;
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = globalClock };
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(origin), Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = TimeSpan.FromSeconds(origin + 2),
            Value = 500,
            Easing = new SplineEasing(1f / 3, 0, 2f / 3, 1)
        });
        scope.Shape.Width.Animation = animation;
        scope.Base.Model.CurrentTime.Value = TimeSpan.FromSeconds(3.007);
        scope.Base.Model.HistoryManager.Commit();
        HeadlessTestHelpers.Render(3);
        Assert.That(icon.Icon, Is.EqualTo(Icon.Diamond));
        Assert.That(icon.IconVariant, Is.EqualTo(IconVariant.Regular));
        Assert.That((string)ToolTip.GetTip(button)!, Does.StartWith(CommandNames.InsertKeyFrame));
        TimeSpan keyTime = TimeSpan.FromSeconds(origin + 1);
        float[] samples = [animation.Interpolate(TimeSpan.FromSeconds(origin + 0.5)), animation.Interpolate(keyTime),
            animation.Interpolate(TimeSpan.FromSeconds(origin + 1.5))];
        int undo = scope.Base.Model.HistoryManager.UndoCount;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        Assert.That(animation.KeyFrames, Has.Count.EqualTo(3));
        Assert.That(animation.KeyFrames[1].KeyTime, Is.EqualTo(keyTime));
        Assert.That(animation.KeyFrames[1].Value, Is.EqualTo(samples[1]).Within(0.001));
        Assert.That(animation.Interpolate(TimeSpan.FromSeconds(origin + 0.5)), Is.EqualTo(samples[0]).Within(0.001));
        Assert.That(animation.Interpolate(TimeSpan.FromSeconds(origin + 1.5)), Is.EqualTo(samples[2]).Within(0.001));
        Assert.That(icon.IconVariant, Is.EqualTo(IconVariant.Filled));
        Assert.That((string)ToolTip.GetTip(button)!, Does.StartWith(CommandNames.RemoveKeyFrame));
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        scope.Capture($"property-tree-keyframe-filled-{globalClock}-{light}");

        scope.Base.Model.CurrentTime.Value = TimeSpan.FromSeconds(3.08);
        HeadlessTestHelpers.Render();
        Assert.That(icon.IconVariant, Is.EqualTo(IconVariant.Regular));
        scope.Capture($"property-tree-keyframe-outline-{globalClock}-{light}");
        scope.Base.Model.CurrentTime.Value = TimeSpan.FromSeconds(3.015);
        HeadlessTestHelpers.Render();
        Assert.That(icon.IconVariant, Is.EqualTo(IconVariant.Filled), "The indicator must use the same frame rounding as key insertion.");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
        Assert.That(item.HasAnimation.Value, Is.True);
        Assert.That(icon.IconVariant, Is.EqualTo(IconVariant.Regular));
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 2));
        scope.Base.Model.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(animation.KeyFrames, Has.Count.EqualTo(3));
        Assert.That(icon.IconVariant, Is.EqualTo(IconVariant.Filled));
        scope.Base.Model.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
        Assert.That(icon.IconVariant, Is.EqualTo(IconVariant.Regular));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Animation_button_context_menu_removes_the_animation_and_undo_restores_it(bool light)
    {
        using var scope = await TreeScope.CreateAsync(light: light);
        var item = scope.Find(scope.Shape.Width);
        var button = scope.AnimationButton(item);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        var animation = scope.Shape.Width.Animation;
        int undo = scope.Base.Model.HistoryManager.UndoCount;
        Point point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), scope.Base.Window)!.Value;
        scope.Base.RightClick(point);
        var menu = (FAMenuFlyout)button.ContextFlyout!;
        Assert.That(menu.IsOpen, Is.True);
        var remove = menu.Items.OfType<FAMenuFlyoutItem>().Single();
        Assert.That(remove.Text, Is.EqualTo(Strings.RemoveAnimation));
        Assert.That(remove.IsEnabled, Is.True);
        Assert.That(scope.Shape.Width.Animation, Is.SameAs(animation));
        scope.Capture($"property-tree-animation-menu-{light}");
        remove.Focus();
        scope.Base.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        scope.Base.Window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        HeadlessTestHelpers.Render(3);
        Assert.That(menu.IsOpen, Is.False);
        Assert.That(scope.Shape.Width.Animation, Is.Null);
        Assert.That(scope.Shape.Width.CurrentValue, Is.EqualTo(240));
        Assert.That(item.HasAnimation.Value, Is.False);
        Assert.That(ToolTip.GetTip(button), Is.EqualTo(Strings.EnableAnimation));
        Assert.That(button.GetVisualDescendants().OfType<FluentIcon>().Single().Icon, Is.EqualTo(Icon.Timer));
        Assert.That(scope.Model.SelectedAnimation.Value, Is.Null);
        Assert.That(scope.Model.SelectedTreeItem.Value, Is.SameAs(item));
        Assert.That(scope.Base.Model.HistoryManager.UndoCount, Is.EqualTo(undo + 1));
        scope.Base.Model.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Shape.Width.Animation, Is.SameAs(animation));
        Assert.That(item.HasAnimation.Value, Is.True);
        Assert.That(item.HasKeyFrame.Value, Is.True);
        Assert.That(scope.Model.SelectedAnimation.Value!.Animation, Is.SameAs(animation));
    }

    [AvaloniaTest]
    public async Task Removing_the_last_key_keeps_animation_enabled_and_clock_mode_changes_update_the_indicator()
    {
        using var scope = await TreeScope.CreateAsync();
        var item = scope.Find(scope.Shape.Width);
        scope.Model.EnableAnimation(item);
        var animation = (KeyFrameAnimation<float>)scope.Shape.Width.Animation!;
        scope.Base.Model.Element!.Start = TimeSpan.FromSeconds(1);
        scope.Base.Model.CurrentTime.Value = TimeSpan.FromSeconds(1);
        HeadlessTestHelpers.Render(3);
        Assert.That(item.HasKeyFrame.Value, Is.True);
        animation.UseGlobalClock = true;
        Assert.That(item.HasKeyFrame.Value, Is.False);
        animation.UseGlobalClock = false;
        Assert.That(item.HasKeyFrame.Value, Is.True);
        scope.Base.Model.HistoryManager.Commit();
        scope.AnimationButton(item).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        Assert.That(animation.KeyFrames, Is.Empty);
        Assert.That(scope.Shape.Width.Animation, Is.SameAs(animation));
        Assert.That(item.HasAnimation.Value, Is.True);
        Assert.That(item.HasKeyFrame.Value, Is.False);
    }

    [AvaloniaTest]
    public async Task Color_channels_share_an_animation_and_preserve_the_active_graph_when_switching()
    {
        using var scope = await TreeScope.CreateAsync();
        var fill = scope.Find(scope.Shape.Fill);
        var color = scope.Find(scope.Brush.Color);
        fill.IsExpanded.Value = color.IsExpanded.Value = true;
        HeadlessTestHelpers.Render(3);
        scope.AnimationButton(color).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        HeadlessTestHelpers.Render(3);
        var animation = (KeyFrameAnimation<Color>)scope.Brush.Color.Animation!;
        Assert.That(animation.KeyFrames.Single().Value, Is.EqualTo(scope.Brush.Color.CurrentValue));
        var graph = scope.Model.SelectedAnimation.Value!;
        var tree = scope.View.FindControl<TreeView>("PropertiesTree")!;
        foreach (var channel in color.Children)
        {
            tree.SelectedItem = channel;
            HeadlessTestHelpers.Render();
            Assert.That(scope.Model.SelectedTreeItem.Value, Is.SameAs(channel));
            Assert.That(scope.Model.SelectedAnimation.Value, Is.SameAs(graph));
            Assert.That(graph.SelectedView.Value!.Name, Is.EqualTo(channel.ChannelName));
        }
        scope.Model.Refresh();
        Assert.That(scope.Model.SelectedAnimation.Value, Is.SameAs(graph));
        Assert.That(graph.SelectedView.Value!.Name, Is.EqualTo("Alpha"));
        scope.Capture("property-tree-color-animation");
    }

    [AvaloniaTest]
    public async Task Replacing_a_nested_object_refreshes_its_properties_without_stale_animations()
    {
        using var scope = await TreeScope.CreateAsync();
        var color = scope.Find(scope.Brush.Color);
        scope.Model.EnableAnimation(color);
        scope.Model.SelectedTreeItem.Value = color.Children[0];
        var replacement = new SolidColorBrush(Colors.Blue);
        scope.Base.Model.HistoryManager.ExecuteInTransaction(() => scope.Shape.Fill.CurrentValue = replacement, "Replace fill");
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Find(replacement.Color), Is.SameAs(color));
        Assert.That(color.HasAnimation.Value, Is.False);
        Assert.That(scope.Model.Items, Is.Empty);
        Assert.That(scope.Model.SelectedAnimation.Value, Is.Null);
        scope.Base.Model.HistoryManager.Undo();
        HeadlessTestHelpers.Render(3);
        Assert.That(scope.Find(scope.Brush.Color), Is.SameAs(color));
        Assert.That(scope.Model.SelectedAnimation.Value!.Animation, Is.SameAs(scope.Brush.Color.Animation));
        Assert.That(scope.Model.SelectedAnimation.Value.SelectedView.Value!.Name, Is.EqualTo("Red"));
    }

    [AvaloniaTest]
    public async Task Dynamic_node_properties_remain_available_and_can_enable_animation()
    {
        using var scope = await TreeScope.CreateAsync();
        var drawable = new NodeGraphDrawable();
        var source = new LayerInputNode();
        var port = new LayerInputNode.LayerInputPort<float>();
        port.SetupProperty("Amount");
        port.Property!.SetValue(42f);
        source.Items.Add(port);
        drawable.Model.CurrentValue!.Nodes.Add(source);
        scope.Base.Model.Element!.Objects.Add(drawable);
        HeadlessTestHelpers.Render(3);
        var item = Flatten(scope.Model.TreeItems).Single(x => ReferenceEquals(x.Adapter, port.Property));
        Assert.That(item.Name.Value, Is.EqualTo(port.Property.DisplayName));
        Assert.That(item.CanAnimate.Value, Is.True);
        scope.Model.EnableAnimation(item);
        var animation = ((IAnimatablePropertyAdapter)port.Property).Animation as KeyFrameAnimation<float>;
        Assert.That(animation, Is.Not.Null);
        Assert.That(animation!.KeyFrames.Single().Value, Is.EqualTo(42));
        Assert.That(scope.Model.SelectedAnimation.Value!.Animation, Is.SameAs(animation));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Effect_and_transform_groups_show_children_directly_including_nested_groups(bool light)
    {
        using var scope = await TreeScope.CreateAsync(light: light);
        var transforms = new TransformGroup();
        var move = new TranslateTransform();
        var nestedTransform = new TransformGroup();
        var scale = new ScaleTransform();
        nestedTransform.Children.Add(scale);
        transforms.Children.Add(move);
        transforms.Children.Add(nestedTransform);
        var effects = new FilterEffectGroup();
        var blur = new Blur();
        var nestedEffect = new FilterEffectGroup();
        var nestedBlur = new Blur();
        nestedEffect.Children.Add(nestedBlur);
        effects.Children.Add(blur);
        effects.Children.Add(nestedEffect);
        scope.Shape.Transform.CurrentValue = transforms;
        scope.Shape.FilterEffect.CurrentValue = effects;
        HeadlessTestHelpers.Render(3);
        var transformRow = scope.Find(scope.Shape.Transform);
        var effectRow = scope.Find(scope.Shape.FilterEffect);
        var moveRow = scope.Find(move.X).Parent!;
        var scaleRow = scope.Find(scale.Scale).Parent!;
        var blurRow = scope.Find(blur.Sigma).Parent!;
        var nestedBlurRow = scope.Find(nestedBlur.Sigma).Parent!;
        Assert.That(transformRow.Children, Is.EqualTo(new[] { moveRow, scaleRow.Parent }));
        Assert.That(effectRow.Children, Is.EqualTo(new[] { blurRow, nestedBlurRow.Parent }));
        Assert.That(moveRow.Parent, Is.SameAs(transformRow));
        Assert.That(scaleRow.Parent!.Parent, Is.SameAs(transformRow));
        Assert.That(blurRow.Parent, Is.SameAs(effectRow));
        Assert.That(nestedBlurRow.Parent!.Parent, Is.SameAs(effectRow));
        Assert.That(Flatten(scope.Model.TreeItems).Any(x => x.Property == transforms.Children
            || x.Property == nestedTransform.Children || x.Property == effects.Children || x.Property == nestedEffect.Children), Is.False);
        transformRow.IsExpanded.Value = true;
        effectRow.IsExpanded.Value = true;
        scaleRow.Parent.IsExpanded.Value = true;
        nestedBlurRow.Parent.IsExpanded.Value = true;
        HeadlessTestHelpers.Render(3);
        scope.Capture($"property-tree-direct-groups-{light}");
    }

    [AvaloniaTest]
    public async Task Group_children_update_in_place_and_keep_the_selected_animation_when_reordered()
    {
        using var scope = await TreeScope.CreateAsync();
        var transforms = new TransformGroup();
        scope.Shape.Transform.CurrentValue = transforms;
        HeadlessTestHelpers.Render(3);
        var group = scope.Find(scope.Shape.Transform);
        Assert.That(group.Children, Is.Empty, "An empty group must not show an empty Children row.");
        var move = new TranslateTransform();
        var scale = new ScaleTransform();
        transforms.Children.Add(move);
        transforms.Children.Add(scale);
        HeadlessTestHelpers.Render(3);
        var property = scope.Find(move.X);
        var moveRow = property.Parent!;
        var scaleRow = scope.Find(scale.Scale).Parent!;
        Assert.That(group.Children, Is.EqualTo(new[] { moveRow, scaleRow }));
        scope.Model.EnableAnimation(property);
        var graph = scope.Model.SelectedAnimation.Value;
        var saved = new JsonObject();
        scope.Model.WriteToJson(saved);
        transforms.Children.Move(0, 1);
        HeadlessTestHelpers.Render(3);
        Assert.That(group.Children, Is.EqualTo(new[] { scaleRow, moveRow }));
        Assert.That(scope.Model.SelectedTreeItem.Value, Is.SameAs(property));
        Assert.That(scope.Model.SelectedAnimation.Value, Is.SameAs(graph));
        using var restored = new GraphEditorTabViewModel(scope.Base.Model.EditorContext);
        restored.ReadFromJson(saved);
        Assert.That(restored.SelectedAnimation.Value!.Animation, Is.SameAs(move.X.Animation));
        Assert.That(restored.SelectedTreeItem.Value!.Parent!.Parent!.Property, Is.SameAs(scope.Shape.Transform));
        transforms.Children.Remove(scale);
        HeadlessTestHelpers.Render(3);
        Assert.That(group.Children, Is.EqualTo(new[] { moveRow }));
        Assert.That(scope.Model.SelectedTreeItem.Value, Is.SameAs(property));
    }

    [AvaloniaTest]
    public async Task Drawable_groups_keep_their_properties_and_show_child_objects_directly()
    {
        using var scope = await TreeScope.CreateAsync();
        var group = new DrawableGroup();
        var child = new EllipseShape();
        group.Children.Add(child);
        scope.Base.Model.Element!.Objects.Add(group);
        HeadlessTestHelpers.Render(3);
        var groupRow = scope.Model.TreeItems.Last();
        Assert.That(scope.Find(child.Width).Parent!.Parent, Is.SameAs(groupRow));
        Assert.That(scope.Find(group.Opacity).Parent, Is.SameAs(groupRow));
        Assert.That(Flatten(scope.Model.TreeItems).Any(x => x.Property == group.Children), Is.False);
    }

    private static IEnumerable<GraphEditorTreeItemViewModel> Flatten(IEnumerable<GraphEditorTreeItemViewModel> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in Flatten(item.Children)) yield return child;
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task Tree_selection_restores_with_or_without_an_animation(bool animated)
    {
        using var scope = await TreeScope.CreateAsync();
        var color = scope.Find(scope.Brush.Color);
        if (animated) scope.Model.EnableAnimation(color);
        scope.Model.SelectedTreeItem.Value = color.Children[2];
        var json = new JsonObject();
        scope.Model.WriteToJson(json);
        using var restored = new GraphEditorTabViewModel(scope.Base.Model.EditorContext);
        restored.ReadFromJson(json);
        Assert.That(restored.Element.Value, Is.SameAs(scope.Base.Model.Element));
        Assert.That(restored.SelectedTreeItem.Value!.ChannelName, Is.EqualTo("Blue"));
        Assert.That(restored.SelectedTreeItem.Value.Parent!.IsExpanded.Value, Is.True);
        Assert.That(restored.SelectedAnimation.Value != null, Is.EqualTo(animated));
        if (animated)
        {
            Assert.That(restored.SelectedAnimation.Value!.SelectedView.Value!.Name, Is.EqualTo("Blue"));
            json.Remove("propertyPath");
            using var legacy = new GraphEditorTabViewModel(scope.Base.Model.EditorContext);
            legacy.ReadFromJson(json);
            Assert.That(legacy.SelectedAnimation.Value!.Animation, Is.SameAs(scope.Brush.Color.Animation));
        }
    }

    private sealed class TreeScope : IDisposable
    {
        public required GraphScope Base { get; init; }
        public required GraphEditorTabViewModel Model { get; init; }
        public required GraphEditorTabView View { get; init; }
        public required EllipseShape Shape { get; init; }
        public required SolidColorBrush Brush { get; init; }

        public static async Task<TreeScope> CreateAsync(int width = 960, bool light = false)
        {
            var graph = await GraphScope.CreateAsync(light);
            graph.View.DataContext = null;
            var brush = new SolidColorBrush(Color.FromArgb(220, 240, 120, 60));
            var shape = new EllipseShape();
            shape.Width.CurrentValue = 240;
            shape.Height.CurrentValue = 160;
            shape.Fill.CurrentValue = brush;
            graph.Model.Element!.Objects.Clear();
            graph.Model.Element.Objects.Add(shape);
            graph.Model.HistoryManager.Commit();
            var model = new GraphEditorTabViewModel(graph.Model.EditorContext);
            model.Element.Value = graph.Model.Element;
            var view = new GraphEditorTabView { DataContext = model };
            graph.Window.Content = view;
            graph.Window.Width = width;
            graph.Window.Height = 680;
            HeadlessTestHelpers.Render(3);
            return new TreeScope { Base = graph, Model = model, View = view, Shape = shape, Brush = brush };
        }

        public GraphEditorTreeItemViewModel Find(IProperty property) => Flatten(Model.TreeItems).Single(x => x.Property == property);

        public Button AnimationButton(GraphEditorTreeItemViewModel item) => View.GetVisualDescendants().OfType<Button>()
            .Single(x => ReferenceEquals(x.DataContext, item) && x.Classes.Contains("graph-animation"));

        public void Capture(string name) => Base.Capture(name);

        public void Dispose()
        {
            View.DataContext = null;
            Model.Dispose();
            Base.Dispose();
        }
    }
}
