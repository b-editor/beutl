using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Language;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using Moq;
using Reactive.Bindings;
using FluentIcon = FluentIcons.Avalonia.Fluent.FluentIcon;
using Point = Avalonia.Point;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Beutl.HeadlessUITests;

public partial class PropertyExpressionTests
{
    [AvaloniaTest]
    [TestCase(0, 1)]
    [TestCase(0, 2)]
    [TestCase(5, 1)]
    [TestCase(5, 2)]
    public void Typing_and_dragging_show_the_expression_at_the_keyframe_being_edited(double start, double localTime)
    {
        using var scope = new KeyFrameEditScope(start, localTime);
        using var pointerLock = PointerLockScope.Off(TestShell.Configuration.EditorConfig);
        using var host = scope.Show();
        var key = (KeyFrame<float>)scope.WidthAnimation.KeyFrames[1];
        Assert.Multiple(() =>
        {
            Assert.That(scope.WidthModel.EditingKeyFrame.Value, Is.SameAs(key));
            Assert.That(scope.WidthEditor.Value, Is.EqualTo(450));
            Assert.That(scope.HeightEditor.Value, Is.EqualTo(300));
            Assert.That(scope.WidthModel.IsAtKeyFrame.Value, Is.EqualTo(localTime == 2));
        });

        TextBox box = scope.WidthEditor.GetVisualDescendants().OfType<TextBox>().Single(x => x.Name == "PART_InnerTextBox");
        Assert.That(box.Text, Is.EqualTo("450"));
        box.Focus();
        Assert.That(box.Text, Is.EqualTo("400"), "Focusing shows the keyframe value before the expression.");
        box.SelectAll();
        host.Window.KeyTextInput("300");
        host.Window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        HeadlessTestHelpers.Render();
        Assert.That(key.Value.Value, Is.EqualTo(300));
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(350));
        Assert.That(box.Text, Is.EqualTo("350"));
        Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        scope.History.Undo();
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(450));

        var slider = scope.WidthEditor.GetVisualDescendants().OfType<ValueSliderField>().Single();
        Point press = HeadlessTestHelpers.Center(slider, host.Window);
        TimelineTestHost.WaitForHitTest(host.Window, slider, press);
        host.Window.MouseMove(press);
        host.Window.MouseDown(press, MouseButton.Left);
        host.Window.MouseMove(press + new Point(4, 0), RawInputModifiers.LeftMouseButton);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(450));
        host.Window.MouseMove(press + new Point(9, 0), RawInputModifiers.LeftMouseButton);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(455));
        Point end = press + new Point(14, 0);
        host.Window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        host.Window.MouseUp(end, MouseButton.Left);
        HeadlessTestHelpers.Render();
        Assert.Multiple(() =>
        {
            Assert.That(key.Value.Value, Is.EqualTo(410));
            Assert.That(scope.WidthEditor.Value, Is.EqualTo(460));
            Assert.That(scope.Shape.Width.Value, Is.EqualTo(200), "The stored value stays below the animation.");
            Assert.That(scope.Shape.Width.Expression!.ExpressionString, Is.EqualTo("Value + 50"));
            Assert.That(scope.History.UndoCount, Is.EqualTo(1));
        });
        HeadlessCapture.SaveIfRequested(host.Window, nameof(PropertyExpressionTests), $"keyframe-drag-{start}-{localTime}");
        scope.History.Undo();
        Assert.That(key.Value.Value, Is.EqualTo(400));
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(450));
    }

    [AvaloniaTest]
    public void Moving_to_another_segment_updates_the_expression_to_the_new_edited_keyframe()
    {
        using var scope = new KeyFrameEditScope(5, 1);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(450));
        scope.Time.Value = MediaTime.FromSeconds(8);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(650));
        Assert.That(scope.WidthModel.EditingKeyFrame.Value, Is.SameAs(scope.WidthAnimation.KeyFrames[2]));
        scope.Time.Value = MediaTime.FromSeconds(5);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(250));
        scope.Time.Value = MediaTime.FromSeconds(6);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(450));
        Assert.That(scope.History.UndoCount, Is.Zero);
    }

    [AvaloniaTest]
    public void The_shown_expression_follows_edits_to_the_key_time_and_value()
    {
        using var scope = new KeyFrameEditScope(5, 1, "Value + (float)Time * 10");
        var key = (KeyFrame<float>)scope.WidthAnimation.KeyFrames[1];
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(420));
        key.KeyTime.Value = MediaTime.FromSeconds(3);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(430));
        key.Value.Value = 500;
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(530));
    }

    [AvaloniaTest]
    public void Owner_moves_and_clock_mode_changes_update_the_keyframes_scene_time()
    {
        using var scope = new KeyFrameEditScope(5, 3, "Value + (float)Time * 10");
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(640));
        scope.Shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(7), MediaTime.FromSeconds(10));
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(420));
        scope.WidthAnimation.UseGlobalClock.Value = true;
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(570));
        scope.Shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(8), MediaTime.FromSeconds(10));
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(560), "The owner's start changes Time even with global key times.");
        Assert.That(scope.Time.Value, Is.EqualTo(MediaTime.FromSeconds(8)));
    }

    [AvaloniaTest]
    public void Removing_all_keys_returns_the_expression_display_to_the_playhead()
    {
        using var scope = new KeyFrameEditScope(5, 1, "(float)Time * 10");
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(20));
        scope.WidthAnimation.KeyFrames.Clear();
        Assert.That(scope.WidthModel.EditingKeyFrame.Value, Is.Null);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(10));
        scope.Time.Value = MediaTime.FromSeconds(8);
        Assert.That(scope.WidthEditor.Value, Is.EqualTo(30));
    }

    [AvaloniaTest]
    public void An_expression_over_separated_components_still_uses_the_playhead()
    {
        var owner = new ComponentTestObject<Beutl.Graphics.Point>();
        ((IModifiableHierarchical)new ComponentTestRoot()).AddChild(owner);
        var animation = new KeyFrameAnimation<Beutl.Graphics.Point>();
        animation.KeyFrames.Add(new KeyFrame<Beutl.Graphics.Point> { Value = { Value = new(100, 200) } });
        animation.KeyFrames.Add(new KeyFrame<Beutl.Graphics.Point>
        {
            KeyTime = { Value = MediaTime.FromSeconds(2) },
            Value = { Value = new(300, 400) },
            Easing = { Value = new LinearEasing() }
        });
        owner.Value.SetKeyFrameAnimation(animation);
        ComponentAnimations.Separate(owner.Value);
        owner.Value.Expression = new StringExpression<Beutl.Graphics.Point>("new Point(Value.X + 50, Value.Y + 50)");
        using var time = new ReactivePropertySlim<MediaTime>(MediaTime.FromSeconds(1));
        var clock = new Mock<IEditorClock>();
        clock.SetupGet(x => x.CurrentTime).Returns(time);
        using var model = VectorEditors.Point(new AnimatablePropertyAdapter<Beutl.Graphics.Point>(owner.Value, owner));
        model.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(IEditorClock), clock.Object)));
        (float, float) shown = default;
        using IDisposable subscription = model.EditValue.Subscribe(value => shown = value);
        Assert.That(shown, Is.EqualTo((250f, 350f)));
        time.Value = MediaTime.FromSeconds(1.5);
        Assert.That(shown, Is.EqualTo((300f, 400f)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Menu_icons_show_animation_and_expression_together_without_growing_the_button(bool light)
    {
        (bool Animation, bool Expression, bool OnKey)[] states =
            [(true, true, false), (true, true, true), (false, true, false), (true, false, false), (true, false, true), (false, false, false)];
        var scopes = new List<KeyFrameEditScope>();
        var menus = new List<PropertyEditorMenu>();
        var rows = new StackPanel { Spacing = 8, Margin = new Thickness(16) };
        try
        {
            foreach (var state in states)
            {
                var scope = new KeyFrameEditScope(0, state.OnKey ? 2 : 1);
                scopes.Add(scope);
                if (!state.Expression) scope.Shape.Width.Expression = null;
                if (!state.Animation) scope.Shape.Width.SetKeyFrameAnimation(null);
                var menu = new PropertyEditorMenu { DataContext = scope.WidthModel };
                menus.Add(menu);
                rows.Children.Add(new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children = { menu, new TextBlock { Text = $"Keys: {state.Animation}, expression: {state.Expression}, on key: {state.OnKey}", VerticalAlignment = VerticalAlignment.Center } }
                });
            }
            using var host = new ControlHost(new Window
            {
                Content = rows,
                Width = 460,
                Height = 240,
                RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark
            });
            for (int i = 0; i < states.Length; i++)
            {
                var state = states[i];
                AssertMenuIcon(menus[i], state.Animation, state.Expression, state.OnKey);
            }
            HeadlessCapture.SaveIfRequested(host.Window, nameof(PropertyExpressionTests), $"animation-expression-icons-{light}");

            scopes[0].Time.Value = MediaTime.FromSeconds(2);
            AssertMenuIcon(menus[0], animation: true, expression: true, onKey: true);
            scopes[0].Shape.Width.Expression = null;
            AssertMenuIcon(menus[0], animation: true, expression: false, onKey: true);
        }
        finally
        {
            foreach (var scope in scopes) scope.Dispose();
        }
    }

    [AvaloniaTest]
    public void A_combined_editor_in_a_group_uses_the_same_animation_and_expression_badge()
    {
        using var scope = new KeyFrameEditScope(0, 1);
        var contexts = new List<IPropertyEditorContext>();
        PropertyEditorResolver.CreateContexts(PropertyAdapterFactory.CreateAdapters(scope.Shape),
            [PropertyEditorExtension.Instance], PropertyEditorSurface.Panel, contexts.Add);
        using var combined = contexts.OfType<CombinedEditorViewModel<float, (float, float)>>().First();
        foreach (IPropertyEditorContext context in contexts)
            if (!ReferenceEquals(context, combined)) context.Dispose();
        combined.Accept(scope.Services);
        Assert.That(combined.SetExpression("new Size(Time * 10, Time * 20)", out string? error), Is.True, error);
        Assert.Multiple(() =>
        {
            Assert.That(scope.Shape.Width.GetKeyFrameAnimation(), Is.SameAs(scope.WidthAnimation));
            Assert.That(scope.Shape.Height.GetKeyFrameAnimation(), Is.SameAs(scope.HeightAnimation));
        });
        using var group = new PropertyEditorGroupContext([combined], "Size", isFirst: true);
        using var host = new ControlHost(new PropertyEditorGroup { DataContext = group }, width: 500, height: 120);
        PropertyEditorMenu menu = host.Window.GetVisualDescendants().OfType<PropertyEditorMenu>().Single(x => x.IsEffectivelyVisible);
        AssertMenuIcon(menu, animation: true, expression: true, onKey: false);
        var editor = host.Window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
        Assert.That((editor.FirstValue, editor.SecondValue), Is.EqualTo((20f, 40f)));
        TextBox[] boxes = editor.GetVisualDescendants().OfType<TextBox>().ToArray();
        Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("20", "40")));
        boxes[0].Focus();
        HeadlessTestHelpers.Render();
        Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("400", "40")),
            "Focusing a combined box shows the keyframe value before the expression.");
        boxes[1].Focus();
        HeadlessTestHelpers.Render();
        Assert.That((boxes[0].Text, boxes[1].Text), Is.EqualTo(("20", "300")));
        scope.Time.Value = MediaTime.FromSeconds(2);
        AssertMenuIcon(menu, animation: true, expression: true, onKey: true);
        HeadlessCapture.SaveIfRequested(host.Window, nameof(PropertyExpressionTests), "combined-animation-expression");
    }

    [AvaloniaTest]
    public void Editing_an_expression_from_the_menu_keeps_the_existing_animation()
    {
        using var scope = new KeyFrameEditScope(0, 1);
        scope.Shape.Height.Expression = null;
        scope.History.Commit();
        int before = scope.History.UndoCount;

        var menu = new PropertyEditorMenu { DataContext = scope.HeightModel, VerticalAlignment = VerticalAlignment.Top };
        using var host = new ControlHost(menu, width: 640, height: 480);
        try
        {
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            var contextMenu = (FAMenuFlyout)button.ContextFlyout!;
            contextMenu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "editExpressionItem")
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            HeadlessTestHelpers.Render();

            var popup = ExpressionEditors(host.Window).Single();
            var presenter = (ExpressionEditorFlyoutPresenter)popup.Child!;
            presenter.GetVisualDescendants().OfType<TextBox>().Single(text => text.Name == "ExpressionTextBox")
                .Text = "Value + 25";
            presenter.GetVisualDescendants().OfType<Button>().Single(accept => accept.Name == "AcceptButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render();

            Assert.Multiple(() =>
            {
                Assert.That(popup.IsOpen, Is.False);
                Assert.That(scope.Shape.Height.HasExpression, Is.True);
                Assert.That(scope.Shape.Height.Expression!.ExpressionString, Is.EqualTo("Value + 25"));
                Assert.That(scope.Shape.Height.GetKeyFrameAnimation(), Is.SameAs(scope.HeightAnimation));
                Assert.That(scope.HeightAnimation.KeyFrames, Has.Count.EqualTo(3));
                Assert.That(scope.HeightEditor.Value, Is.EqualTo(325f));
                Assert.That(scope.History.UndoCount, Is.EqualTo(before + 1));
            });

            AssertMenuIcon(menu, animation: true, expression: true, onKey: false);

            scope.History.Undo();
            HeadlessTestHelpers.Render();
            Assert.Multiple(() =>
            {
                Assert.That(scope.Shape.Height.HasExpression, Is.False);
                Assert.That(scope.Shape.Height.GetKeyFrameAnimation(), Is.SameAs(scope.HeightAnimation));
                Assert.That(scope.HeightAnimation.KeyFrames, Has.Count.EqualTo(3));
                Assert.That(scope.HeightEditor.Value, Is.EqualTo(300f));
            });
        }
        finally
        {
            foreach (var popup in host.Window.OpenedPopups.ToArray())
                popup.Close();
        }
    }

    [AvaloniaTest]
    public void Clicking_the_diamond_with_animation_and_expression_inserts_and_removes_keyframes()
    {
        using var scope = new KeyFrameEditScope(0, 1);
        var menu = new PropertyEditorMenu { DataContext = scope.WidthModel, VerticalAlignment = VerticalAlignment.Top };
        using var host = new ControlHost(menu, width: 640, height: 480);
        try
        {
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            AssertMenuIcon(menu, animation: true, expression: true, onKey: false);
            Assert.That(scope.WidthAnimation.KeyFrames, Has.Count.EqualTo(3));

            // Off a keyframe: clicking the diamond inserts a keyframe instead of opening the expression popup.
            HeadlessTestHelpers.Click(button);
            Assert.Multiple(() =>
            {
                Assert.That(ExpressionEditors(host.Window), Is.Empty);
                Assert.That(scope.WidthAnimation.KeyFrames, Has.Count.EqualTo(4));
                Assert.That(scope.WidthAnimation.KeyFrames.Select(k => k.KeyTime.Value), Does.Contain(MediaTime.FromSeconds(1)));
                Assert.That(scope.Shape.Width.HasExpression, Is.True);
                Assert.That(scope.Shape.Width.Expression!.ExpressionString, Is.EqualTo("Value + 50"));
            });
            AssertMenuIcon(menu, animation: true, expression: true, onKey: true);

            // On a keyframe: clicking the filled diamond removes that keyframe instead of opening the expression popup.
            HeadlessTestHelpers.Click(button);
            Assert.Multiple(() =>
            {
                Assert.That(ExpressionEditors(host.Window), Is.Empty);
                Assert.That(scope.WidthAnimation.KeyFrames, Has.Count.EqualTo(3));
                Assert.That(scope.WidthAnimation.KeyFrames.Select(k => k.KeyTime.Value), Does.Not.Contain(MediaTime.FromSeconds(1)));
                Assert.That(scope.Shape.Width.HasExpression, Is.True);
                Assert.That(scope.Shape.Width.Expression!.ExpressionString, Is.EqualTo("Value + 50"));
            });
            AssertMenuIcon(menu, animation: true, expression: true, onKey: false);
        }
        finally
        {
            foreach (var popup in host.Window.OpenedPopups.ToArray())
                popup.Close();
        }
    }

    private static void AssertMenuIcon(PropertyEditorMenu menu, bool animation, bool expression, bool onKey)
    {
        FluentIcon icon = menu.GetVisualDescendants().OfType<FluentIcon>().Single(x => x.Name == "symbolIcon");
        FluentIcon badge = menu.GetVisualDescendants().OfType<FluentIcon>().Single(x => x.Name == "expressionBadge");
        Button button = menu.GetVisualDescendants().OfType<Button>().First();
        Assert.Multiple(() =>
        {
            Assert.That(icon.Icon, Is.EqualTo(animation ? Icon.Diamond : expression ? Icon.MathFormula : Icon.MoreVertical));
            Assert.That(icon.IconVariant, Is.EqualTo(onKey ? IconVariant.Filled : IconVariant.Regular));
            Assert.That(badge.IsVisible, Is.EqualTo(animation && expression));
            Assert.That(badge.Icon, Is.EqualTo(Icon.MathFormula));
            Assert.That(button.Bounds.Size, Is.EqualTo(new Avalonia.Size(24, 24)));
            string? tip = ToolTip.GetTip(menu) as string;
            if (animation) Assert.That(tip, Does.Contain(MessageStrings.AnimationIsEnabled));
            if (expression) Assert.That(tip, Does.Contain(MessageStrings.ExpressionIsSet));
            if (animation || expression)
            {
                Assert.That(menu.TryFindResource(animation ? "SystemFillColorCaution" : "SystemFillColorSuccess", menu.ActualThemeVariant, out object? fill), Is.True);
                Assert.That(((Avalonia.Media.ISolidColorBrush)icon.Foreground!).Color, Is.EqualTo(fill));
            }
            if (animation && expression)
            {
                Assert.That(menu.TryFindResource("SystemFillColorSuccess", menu.ActualThemeVariant, out object? fill), Is.True);
                Assert.That(((Avalonia.Media.ISolidColorBrush)badge.Foreground!).Color, Is.EqualTo(fill));
                Assert.That(badge.IsHitTestVisible, Is.False);
            }
        });
    }

    private sealed class KeyFrameEditScope : IDisposable
    {
        private readonly PropertyEditorTestHost.HistoryScope _history;

        public KeyFrameEditScope(double start, double localTime, string expression = "Value + 50")
        {
            ((IModifiableHierarchical)new ComponentTestRoot()).AddChild(Shape);
            Shape.IsTimeAnchor.Value = true;
            Shape.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(start), MediaTime.FromSeconds(10));
            Shape.Width.Value = 200;
            Shape.Height.Value = 100;
            WidthAnimation = CreateAnimation(200, 400, 600);
            HeightAnimation = CreateAnimation(100, 300, 500);
            Shape.Width.SetKeyFrameAnimation(WidthAnimation);
            Shape.Height.SetKeyFrameAnimation(HeightAnimation);
            Shape.Width.Expression = new StringExpression<float>(expression);
            Time = new ReactivePropertySlim<MediaTime>(MediaTime.FromSeconds(start + localTime));
            var clock = new Mock<IEditorClock>();
            clock.SetupGet(x => x.CurrentTime).Returns(Time);
            _history = new PropertyEditorTestHost.HistoryScope(Shape);
            Services = new PropertyEditorTestHost.ServiceVisitor((typeof(HistoryManager), History), (typeof(IEditorClock), clock.Object));
            (var widthModel, Control widthControl) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(Shape.Width, Shape));
            (var heightModel, Control heightControl) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(Shape.Height, Shape));
            WidthModel = (NumberEditorViewModel<float>)widthModel;
            HeightModel = (NumberEditorViewModel<float>)heightModel;
            WidthEditor = (NumberSliderEditor<float>)widthControl;
            HeightEditor = (NumberSliderEditor<float>)heightControl;
            WidthModel.Accept(Services);
            HeightModel.Accept(Services);
        }

        public RectShape Shape { get; } = new();
        public KeyFrameAnimation<float> WidthAnimation { get; }
        public KeyFrameAnimation<float> HeightAnimation { get; }
        public ReactivePropertySlim<MediaTime> Time { get; }
        public PropertyEditorTestHost.ServiceVisitor Services { get; }
        public NumberEditorViewModel<float> WidthModel { get; }
        public NumberEditorViewModel<float> HeightModel { get; }
        public NumberSliderEditor<float> WidthEditor { get; }
        public NumberSliderEditor<float> HeightEditor { get; }
        public HistoryManager History => _history.History;

        private static KeyFrameAnimation<float> CreateAnimation(params float[] values)
        {
            var animation = new KeyFrameAnimation<float>();
            for (int i = 0; i < values.Length; i++)
                animation.KeyFrames.Add(new KeyFrame<float>
                {
                    KeyTime = { Value = MediaTime.FromSeconds(i * 2) },
                    Value = { Value = values[i] },
                    Easing = { Value = new LinearEasing() }
                });
            return animation;
        }

        public ControlHost Show()
            => new(new Border
            {
                Focusable = true,
                Child = new StackPanel { Children = { WidthEditor, HeightEditor } }
            }, width: 500, height: 100);

        public void Dispose()
        {
            WidthModel.Dispose();
            HeightModel.Dispose();
            Time.Dispose();
            _history.Dispose();
        }
    }
}
