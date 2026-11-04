using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using Moq;
using Reactive.Bindings;
using FluentIcon = FluentIcons.Avalonia.Fluent.FluentIcon;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Beutl.HeadlessUITests;

// Two properties that one vector editor shows while they share their timing, and that take a row each otherwise.
[TestFixture]
public sealed class CombinedEditorTests
{
    private static IPropertyEditorContext[] CreateContexts(EngineObject owner)
    {
        var contexts = new List<IPropertyEditorContext>();
        PropertyEditorResolver.CreateContexts(
            PropertyAdapterFactory.CreateAdapters(owner), [PropertyEditorExtension.Instance], PropertyEditorSurface.Panel, contexts.Add);
        return [.. contexts];
    }

    private static CombinedEditorViewModel<float> CreateCombined(EngineObject owner)
    {
        IPropertyEditorContext[] contexts = CreateContexts(owner);
        var combined = contexts.OfType<CombinedEditorViewModel<float>>().First();
        foreach (IPropertyEditorContext context in contexts)
        {
            if (!ReferenceEquals(context, combined)) context.Dispose();
        }

        return combined;
    }

    private static Control CreateControl(CombinedEditorViewModel<float> context)
    {
        Assert.That(PropertyEditorHost.TryCreateControl(context, PropertyEditorSurface.Panel, out Control? control), Is.True);
        control!.DataContext = context;
        return control;
    }

    private static KeyFrameAnimation<float> Animation(params double[] seconds)
    {
        var animation = new KeyFrameAnimation<float>();
        foreach (double time in seconds)
            animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(time) }, Value = { Value = (float)time } });
        return animation;
    }

    private static PropertyEditorTestHost.ServiceVisitor Services(HistoryManager history, IReadOnlyReactiveProperty<MediaTime>? time = null)
    {
        var clock = new Mock<IEditorClock>();
        clock.SetupGet(value => value.CurrentTime).Returns(time ?? new ReactivePropertySlim<MediaTime>());
        return new PropertyEditorTestHost.ServiceVisitor((typeof(HistoryManager), history), (typeof(IEditorClock), clock.Object));
    }

    private static bool IsSeparated(IPropertyEditorContext context)
    {
        HeadlessTestHelpers.Settle();
        bool separated = false;
        using IDisposable _ = ((PropertyEditorViewModel)context).SeparatedProperties.Subscribe(value => separated = value != null);
        return separated;
    }

    [AvaloniaTest]
    public void Two_properties_take_one_row_in_place_of_the_first()
    {
        using var scale = new DisposableContexts(CreateContexts(new ScaleTransform()));
        Assert.That(scale.Contexts, Has.Length.EqualTo(2));
        Assert.That(scale.Contexts[0], Is.TypeOf<NumberEditorViewModel<float>>(), "The scale before the pair stays first.");
        var combined = (CombinedEditorViewModel<float>)scale.Contexts[1];
        Assert.That(combined.EditedProperties.Select(p => p.GetEngineProperty()!.Name), Is.EqualTo(new[] { "ScaleX", "ScaleY" }));
        Assert.That(combined.Header, Is.EqualTo($"{GraphicsStrings.ScaleTransform_ScaleXY} (%)"));

        using var shape = new DisposableContexts(CreateContexts(new RectShape()));
        Assert.That(shape.Contexts[0], Is.TypeOf<CombinedEditorViewModel<float>>());
        Assert.That(((PropertyEditorViewModel)shape.Contexts[0]).Header, Is.EqualTo(Strings.Size));
    }

    private sealed class DisposableContexts(IPropertyEditorContext[] contexts) : IDisposable
    {
        public IPropertyEditorContext[] Contexts { get; } = contexts;

        public void Dispose()
        {
            foreach (IPropertyEditorContext context in Contexts) context.Dispose();
        }
    }

    // A pair that names a missing property, mixes value types or cannot animate would silently keep its two rows.
    [AvaloniaTest]
    public void Every_marked_property_makes_a_row_with_the_property_it_names()
    {
        Type[] types = typeof(EngineObject).Assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, ContainsGenericParameters: false }
                && typeof(EngineObject).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) != null
                && type.GetProperties().Any(property => property.GetCustomAttribute<CombinedEditorAttribute>() != null))
            .ToArray();
        Assert.That(types, Has.Length.GreaterThan(10));

        foreach (Type type in types)
        {
            var owner = (EngineObject)Activator.CreateInstance(type)!;
            IProperty[] marked = owner.GetDisplayProperties()
                .Where(property => property.GetAttributes()?.OfType<CombinedEditorAttribute>().Any() == true)
                .ToArray();
            Assert.That(marked, Is.Not.Empty, type.FullName);
            using var contexts = new DisposableContexts(CreateContexts(owner));
            foreach (IProperty property in marked)
            {
                CombinedEditorAttribute attribute = property.GetAttributes()!.OfType<CombinedEditorAttribute>().Single();
                Assert.That(attribute.GetName(), Is.Not.Null.And.Not.Empty, $"{type.Name}.{property.Name}");
                var row = contexts.Contexts.OfType<PropertyEditorViewModel>()
                    .SingleOrDefault(context => context.EditedProperties.Any(p => ReferenceEquals(p.GetEngineProperty(), property)));
                Assert.That(row?.EditedProperties.Select(p => p.GetEngineProperty()!.Name),
                    Is.EqualTo(new[] { property.Name, attribute.Second }), $"{type.Name}.{property.Name}");
            }
        }
    }

    [AvaloniaTest]
    public void The_values_show_with_the_names_of_their_axes()
    {
        var transform = new ScaleTransform(30, 40);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        var editor = (Vector2Editor<float>)((StackPanel)CreateControl(combined)).Children[0];

        Assert.Multiple(() =>
        {
            Assert.That(editor.FirstValue, Is.EqualTo(30));
            Assert.That(editor.SecondValue, Is.EqualTo(40));
            Assert.That(editor.FirstHeader, Is.EqualTo("X"));
            Assert.That(editor.SecondHeader, Is.EqualTo("Y"));
        });

        transform.ScaleY.Value = 75;
        Assert.That(editor.SecondValue, Is.EqualTo(75), "An edit made elsewhere shows.");
    }

    [AvaloniaTest]
    public void Width_and_height_keep_their_own_names()
    {
        var shape = new RectShape();
        using PropertyEditorTestHost.HistoryScope history = new(shape);
        using CombinedEditorViewModel<float> combined = CreateCombined(shape);
        combined.Accept(Services(history.History));
        var editor = (Vector2Editor<float>)((StackPanel)CreateControl(combined)).Children[0];

        Assert.That(editor.FirstHeader, Is.EqualTo(Strings.Width));
        Assert.That(editor.SecondHeader, Is.EqualTo(Strings.Height));
    }

    [AvaloniaTest]
    public void An_edit_writes_the_values_of_both_as_one_history_entry()
    {
        var transform = new TranslateTransform(1, 2);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;

        Assert.That(combined.PreviewEdit((10, 20)), Is.EqualTo((10f, 20f)));
        combined.ConfirmEdit((1, 2), (10, 20));

        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((10f, 20f)));
        Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
        history.History.Undo();
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((1f, 2f)));
    }

    [AvaloniaTest]
    public void Typing_in_a_box_changes_only_its_property()
    {
        var transform = new TranslateTransform(1, 2);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;
        var window = new Window { Width = 480, Height = 200, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            TextBox[] boxes = window.GetVisualDescendants().OfType<TextBox>().ToArray();
            boxes[0].Focus();
            boxes[0].SelectAll();
            window.KeyTextInput("75");
            boxes[1].Focus();
            HeadlessTestHelpers.Render(3);

            Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((75f, 2f)));
            Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
            history.History.Undo();
            Assert.That(transform.X.Value, Is.EqualTo(1));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Each_value_follows_its_own_range()
    {
        var shape = new RectShape();
        using PropertyEditorTestHost.HistoryScope history = new(shape);
        using CombinedEditorViewModel<float> combined = CreateCombined(shape);
        combined.Accept(Services(history.History));

        Assert.That(combined.PreviewEdit((-5, 7)), Is.EqualTo((0f, 7f)));
        Assert.That((shape.Width.Value, shape.Height.Value), Is.EqualTo((0f, 7f)));
    }

    [AvaloniaTest]
    public void Reset_returns_both_to_their_defaults_as_one_history_entry()
    {
        var shape = new RectShape { Width = { Value = 5 }, Height = { Value = 6 } };
        using PropertyEditorTestHost.HistoryScope history = new(shape);
        using CombinedEditorViewModel<float> combined = CreateCombined(shape);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;

        combined.Reset();

        Assert.That((shape.Width.Value, shape.Height.Value), Is.EqualTo((100f, 100f)));
        Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
    }

    [AvaloniaTest]
    public void Keyframe_commands_run_on_both_as_one_history_entry()
    {
        var transform = new TranslateTransform(1, 2);
        using var scope = new AnimatedScope(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(scope.History.History, scope.Time));
        scope.History.History.Commit();
        int before = scope.History.History.UndoCount;

        combined.PrepareToEditAnimation();
        Assert.Multiple(() =>
        {
            Assert.That(transform.X.GetKeyFrameAnimation()!.KeyFrames.Select(k => k.Value), Is.EqualTo(new[] { 1f }));
            Assert.That(transform.Y.GetKeyFrameAnimation()!.KeyFrames.Select(k => k.Value), Is.EqualTo(new[] { 2f }));
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(combined.HasAnimation.Value, Is.True);
            Assert.That(IsSeparated(combined), Is.False, "The row stays: both have one keyframe at the same time.");
        });

        combined.InsertKeyFrame(MediaTime.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(transform.X.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(transform.Y.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 2));
            Assert.That(IsSeparated(combined), Is.False);
        });

        combined.RemoveKeyFrame(MediaTime.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(transform.X.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(1));
            Assert.That(transform.Y.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(1));
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 3));
        });

        combined.RemoveAnimation();
        Assert.Multiple(() =>
        {
            Assert.That(transform.X.GetKeyFrameAnimation(), Is.Null);
            Assert.That(transform.Y.GetKeyFrameAnimation(), Is.Null);
            Assert.That(combined.HasAnimation.Value, Is.False);
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 4));
        });

        scope.History.History.Undo();
        Assert.That(transform.X.GetKeyFrameAnimation(), Is.Not.Null);
        Assert.That(transform.Y.GetKeyFrameAnimation(), Is.Not.Null);
        Assert.That(IsSeparated(combined), Is.False);
    }

    [AvaloniaTest]
    public void The_diamond_adds_and_removes_the_keyframe_of_both()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 2));
        transform.Y.SetKeyFrameAnimation(Animation(0, 2));
        using var scope = new AnimatedScope(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(scope.History.History, scope.Time));
        scope.History.History.Commit();
        int before = scope.History.History.UndoCount;
        var window = new Window { Width = 480, Height = 200, Content = CreateControl(combined) };
        try
        {
            window.Show();
            scope.Time.Value = MediaTime.FromSeconds(1);
            HeadlessTestHelpers.Render(3);
            var editor = window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
            Button diamond = ((Control)editor.MenuContent!).GetVisualDescendants().OfType<Button>().First();

            diamond.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            Assert.That(transform.X.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(3));
            Assert.That(transform.Y.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(3));
            Assert.That(combined.IsAtKeyFrame.Value, Is.True);
            Assert.That(editor.IsEffectivelyVisible, Is.True);
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 1));

            diamond.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            Assert.That(transform.X.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(transform.Y.GetKeyFrameAnimation()!.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(combined.IsAtKeyFrame.Value, Is.False);
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 2));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void An_edit_at_a_keyframe_writes_that_keyframe_of_each_property()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 2));
        transform.Y.SetKeyFrameAnimation(Animation(0, 2));
        using var scope = new AnimatedScope(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(scope.History.History, scope.Time));

        scope.Time.Value = MediaTime.FromSeconds(2);
        Assert.That(combined.IsAtKeyFrame.Value, Is.True);
        combined.PreviewEdit((7, 8));

        Assert.That(transform.X.GetKeyFrameAnimation()!.KeyFrames.Select(k => k.Value), Is.EqualTo(new[] { 0f, 7f }));
        Assert.That(transform.Y.GetKeyFrameAnimation()!.KeyFrames.Select(k => k.Value), Is.EqualTo(new[] { 0f, 8f }));
    }

    [AvaloniaTest]
    public void The_keyframe_arrows_seek_with_the_first_property_s_keyframes()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 2));
        transform.Y.SetKeyFrameAnimation(Animation(0, 2));
        using var scope = new AnimatedScope(transform);
        var clock = new Mock<IEditorClock>();
        clock.SetupGet(value => value.CurrentTime).Returns(scope.Time);
        clock.Setup(value => value.Seek(It.IsAny<MediaTime>())).Callback<MediaTime>(time => scope.Time.Value = time);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(HistoryManager), scope.History.History), (typeof(IEditorClock), clock.Object)));

        Assert.That(combined.KeyFrameCount.Value, Is.EqualTo(2));
        combined.KeyFrameIndex.Value = 1;

        Assert.That(scope.Time.Value, Is.EqualTo(MediaTime.FromSeconds(2)));
        Assert.That(combined.IsAtKeyFrame.Value, Is.True);
        combined.PreviewEdit((7, 8));
        Assert.That(transform.Y.GetKeyFrameAnimation()!.KeyFrames.Select(k => k.Value), Is.EqualTo(new[] { 0f, 8f }));
    }

    public static IEnumerable<TestCaseData> Differences()
    {
        yield return new TestCaseData(new Action<TranslateTransform>(t => t.X.SetKeyFrameAnimation(Animation(0, 1))))
            .SetArgDisplayNames("One_has_keyframes");
        yield return new TestCaseData(new Action<TranslateTransform>(t => t.Y.Expression = Expression.Create<float>("1f")))
            .SetArgDisplayNames("One_has_an_expression");
        yield return new TestCaseData(new Action<TranslateTransform>(t =>
        {
            t.X.SetKeyFrameAnimation(Animation(0, 1));
            t.Y.SetKeyFrameAnimation(Animation(0, 2));
        })).SetArgDisplayNames("Keyframes_at_other_times");
        yield return new TestCaseData(new Action<TranslateTransform>(t =>
        {
            t.X.SetKeyFrameAnimation(Animation(0, 1));
            t.Y.SetKeyFrameAnimation(Animation(0, 1));
            t.Y.GetKeyFrameAnimation()!.UseGlobalClock.Value = true;
        })).SetArgDisplayNames("Another_clock");
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(Differences))]
    public void A_row_for_each_shows_while_the_two_differ(Action<TranslateTransform> differ)
    {
        var transform = new TranslateTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        var window = new Window { Width = 480, Height = 300, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var editor = window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
            Assert.That(editor.IsEffectivelyVisible, Is.True);
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);

            differ(transform);
            HeadlessTestHelpers.Render(3);

            Assert.That(editor.IsEffectivelyVisible, Is.False);
            NumberEditor<float>[] rows = window.GetVisualDescendants().OfType<NumberEditor<float>>().ToArray();
            Assert.That(rows.Select(row => row.Header), Is.EqualTo(new[] { "X", "Y" }));
            Assert.That(rows.All(row => row.IsEffectivelyVisible && row.MenuContent != null), Is.True);
            Assert.That(combined.HasAnimation.Value, Is.False, "The header's menu does not edit keyframes.");
            Assert.That(combined.CanUnseparateComponents.Value, Is.False);

            transform.X.Expression = null;
            transform.Y.Expression = null;
            transform.X.SetKeyFrameAnimation(null);
            transform.Y.SetKeyFrameAnimation(null);
            HeadlessTestHelpers.Render(3);

            Assert.That(editor.IsEffectivelyVisible, Is.True, "The row returns once they match.");
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void Moving_one_keyframe_splits_the_row_and_moving_it_back_rejoins_it()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 1));
        transform.Y.SetKeyFrameAnimation(Animation(0, 1));
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        Assert.That(IsSeparated(combined), Is.False);

        IKeyFrame key = transform.Y.GetKeyFrameAnimation()!.KeyFrames[1];
        key.KeyTime.Value = MediaTime.FromSeconds(3);
        Assert.That(IsSeparated(combined), Is.True);

        key.KeyTime.Value = MediaTime.FromSeconds(1);
        Assert.That(IsSeparated(combined), Is.False);

        transform.X.GetKeyFrameAnimation()!.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(4) } });
        Assert.That(IsSeparated(combined), Is.True, "Adding a keyframe to one differs too.");
    }

    // The edit that makes the two match runs in the row's own editor, which rejoining the rows disposes.
    [AvaloniaTest]
    public void A_row_s_own_edit_can_rejoin_the_rows()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 1));
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;
        var window = new Window { Width = 480, Height = 300, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var row = (PropertyEditorViewModel)window.GetVisualDescendants().OfType<NumberEditor<float>>().First().DataContext!;

            row.RemoveAnimation();
            HeadlessTestHelpers.Render(3);

            Assert.That(transform.X.GetKeyFrameAnimation(), Is.Null);
            Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single().IsEffectivelyVisible, Is.True);
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
            Assert.That(row.IsDisposed, Is.True);

            history.History.Undo();
            HeadlessTestHelpers.Render(3);
            Assert.That(transform.X.GetKeyFrameAnimation(), Is.Not.Null);
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>().Count(e => e.IsEffectivelyVisible), Is.EqualTo(2));
        }
        finally { window.Close(); }
    }

    // The menu fills its icon on a keyframe, but the header above the two rows has no keyframes to mark.
    [AvaloniaTest]
    public void The_menu_marks_a_keyframe_only_while_the_row_stands_for_both()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 2));
        transform.Y.SetKeyFrameAnimation(Animation(0, 2));
        using var scope = new AnimatedScope(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(scope.History.History, scope.Time));
        var window = new Window { Width = 480, Height = 300, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcon().IconVariant, Is.EqualTo(IconVariant.Filled));

            combined.SeparateComponents();
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcon().Icon, Is.EqualTo(Icon.MoreVertical));
            Assert.That(MenuIcon().IconVariant, Is.EqualTo(IconVariant.Regular));

            combined.UnseparateComponents();
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcon().IconVariant, Is.EqualTo(IconVariant.Filled));

            transform.Y.SetKeyFrameAnimation(null);
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcon().Icon, Is.EqualTo(Icon.MoreVertical));
            Assert.That(MenuIcon().IconVariant, Is.EqualTo(IconVariant.Regular));
        }
        finally { window.Close(); }

        FluentIcon MenuIcon() => window.GetVisualDescendants().OfType<PropertyEditorMenu>()
            .Single(menu => ReferenceEquals(menu.DataContext, combined) && menu.IsEffectivelyVisible)
            .GetVisualDescendants().OfType<FluentIcon>().Single();
    }

    [AvaloniaTest]
    public void Separating_takes_no_history_entry_and_the_choice_is_saved()
    {
        var transform = new TranslateTransform(1, 2);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;

        combined.SeparateComponents();
        Assert.That(IsSeparated(combined), Is.True);
        Assert.That(history.History.UndoCount, Is.EqualTo(before));

        var json = new JsonObject();
        combined.WriteToJson(json);
        using CombinedEditorViewModel<float> restored = CreateCombined(transform);
        Assert.That(IsSeparated(restored), Is.False);
        restored.ReadFromJson(json);
        Assert.That(IsSeparated(restored), Is.True);

        transform.X.SetKeyFrameAnimation(Animation(0, 1));
        Assert.That(combined.CanUnseparateComponents.Value, Is.False);
        combined.UnseparateComponents();
        Assert.That(IsSeparated(combined), Is.True, "It cannot rejoin while they differ.");

        transform.X.SetKeyFrameAnimation(null);
        Assert.That(IsSeparated(combined), Is.True, "The user's choice holds after they match again.");
        Assert.That(combined.CanUnseparateComponents.Value, Is.True);
        combined.UnseparateComponents();
        Assert.That(IsSeparated(combined), Is.False);
        Assert.That((transform.X.Value, transform.Y.Value), Is.EqualTo((1f, 2f)));
    }

    [AvaloniaTest]
    public void The_menu_offers_what_applies_to_both()
    {
        var transform = new TranslateTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        var window = new Window { Width = 480, Height = 400, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var editor = window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
            FAMenuFlyout menu = OpenMenu((Control)editor.MenuContent!);
            Assert.Multiple(() =>
            {
                Assert.That(Item(menu, "separateComponentsItem").IsVisible, Is.True);
                Assert.That(Item(menu, "unseparateComponentsItem").IsVisible, Is.False);
                Assert.That(Item(menu, "editAnimationItem").IsVisible, Is.True);
                Assert.That(Item(menu, "editInlineAnimationItem").IsVisible, Is.True);
                Assert.That(Item(menu, "removeAnimationItem").IsVisible, Is.True);
                Assert.That(Item(menu, "editExpressionItem").IsVisible, Is.False, "An expression belongs to one property.");
                Assert.That(Item(menu, "copyPropertyPathItem").IsVisible, Is.False, "A path names one property.");
                Assert.That(menu.Items.OfType<FAToggleMenuFlyoutItem>().Single(i => i.Name == "uniformEditorToggle").IsVisible, Is.True);
            });
            Item(menu, "separateComponentsItem").RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            menu.Hide();
            HeadlessTestHelpers.Render(3);

            Assert.That(window.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == combined.Header && text.IsEffectivelyVisible), Is.True, "The rows have the name above them.");
            var header = window.GetVisualDescendants().OfType<PropertyEditorMenu>()
                .Single(item => ReferenceEquals(item.DataContext, combined) && item.IsEffectivelyVisible);
            FAMenuFlyout headerMenu = OpenMenu(header);
            FAMenuFlyoutItem unseparate = Item(headerMenu, "unseparateComponentsItem");
            Assert.Multiple(() =>
            {
                Assert.That(Item(headerMenu, "separateComponentsItem").IsVisible, Is.False);
                Assert.That(Item(headerMenu, "editAnimationItem").IsVisible, Is.False);
                Assert.That(unseparate.IsVisible, Is.True);
                Assert.That(unseparate.IsEnabled, Is.True);
                Assert.That(ToolTip.GetTip(unseparate), Is.EqualTo(Strings.UnseparateComponents_SameTiming));
            });
            headerMenu.Hide();

            transform.Y.Expression = Expression.Create<float>("1f");
            HeadlessTestHelpers.Render(3);
            headerMenu = OpenMenu(header);
            Assert.That(Item(headerMenu, "unseparateComponentsItem").IsEnabled, Is.False);
            headerMenu.Hide();
            HeadlessTestHelpers.Render(3);
        }
        finally { window.Close(); }

        static FAMenuFlyout OpenMenu(Control menu)
        {
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            return (FAMenuFlyout)button.ContextFlyout!;
        }

        static FAMenuFlyoutItem Item(FAMenuFlyout menu, string name)
            => menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == name);
    }

    [AvaloniaTest]
    public async Task The_inline_view_shows_both_properties()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"combined-inline-{Guid.NewGuid():N}");
        var shape = new RectShape();
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
        Element element = editor.Scene.Children.Single();
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(element);
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        var combined = tab.Items[0].Properties.OfType<CombinedEditorViewModel<float>>().First();
        TimelineTabViewModel timeline = editor.FindToolTab<TimelineTabViewModel>()!;
        var menu = new PropertyEditorMenu { DataContext = combined };
        var window = new Window { Width = 320, Height = 240, Content = menu };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            var flyout = (FAMenuFlyout)button.ContextFlyout!;
            flyout.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "editInlineAnimationItem")
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            flyout.Hide();
            HeadlessTestHelpers.Render(3);

            Assert.That(timeline.Inlines.Select(inline => inline.Property.GetEngineProperty()),
                Is.EqualTo(new IProperty[] { shape.Width, shape.Height }));
            Assert.That(timeline.Inlines.Select(inline => inline.Property.Animation),
                Is.EqualTo(new[] { shape.Width.GetKeyFrameAnimation(), shape.Height.GetKeyFrameAnimation() }));
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task A_reveal_finds_the_row_by_either_property()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"combined-reveal-{Guid.NewGuid():N}");
        var shape = new RectShape();
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
        Element element = editor.Scene.Children.Single();
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(element);
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        EngineObjectPropertyViewModel item = tab.Items[0];
        var combined = item.Properties.OfType<CombinedEditorViewModel<float>>().First();

        Assert.That(item.FindPropertyEditor(shape, nameof(RectShape.Width)), Is.SameAs(combined));
        Assert.That(item.FindPropertyEditor(shape, nameof(RectShape.Height)), Is.SameAs(combined));
    }

    [AvaloniaTest]
    public void Each_property_keeps_its_own_row_and_host_in_a_node()
    {
        var transform = new TranslateTransform(1, 2);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float> combined = CreateCombined(transform);
        var host = new Mock<IPropertyEditorControlHost>();
        var childHost = new Mock<IPropertyEditorControlHost>();
        host.Setup(value => value.CreateChildHost(It.IsAny<IPropertyAdapter>())).Returns(childHost.Object);
        childHost.Setup(value => value.WrapEditor(It.IsAny<IPropertyEditorContext>(), It.IsAny<Control>()))
            .Returns<IPropertyEditorContext, Control>((_, editor) => new Border { Child = editor });
        combined.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(HistoryManager), history.History), (typeof(IPropertyEditorControlHost), host.Object)));

        Assert.That(combined.GetService(typeof(IPropertyEditorControlHost)), Is.SameAs(host.Object),
            "The hosts of the properties follow from the object that holds them.");
        Assert.That(IsSeparated(combined), Is.True);
        var window = new Window { Width = 480, Height = 300, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);

            Assert.That(window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single().IsEffectivelyVisible, Is.False);
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>().Select(row => row.Header),
                Is.EqualTo(new[] { "X", "Y" }));
            Assert.That(window.GetVisualDescendants().OfType<Border>().Count(border => border.Child is NumberEditor<float>), Is.EqualTo(2));
            Assert.That(window.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == combined.Header && text.IsEffectivelyVisible), Is.False);
            Assert.That(window.GetVisualDescendants().OfType<TreeLineDecorator>().Single().IndentLevel, Is.Zero);
        }
        finally { window.Close(); }
    }

    private sealed class AnimatedScope : IDisposable
    {
        public AnimatedScope(EngineObject owner)
        {
            History = new PropertyEditorTestHost.HistoryScope(owner);
        }

        public PropertyEditorTestHost.HistoryScope History { get; }

        public ReactivePropertySlim<MediaTime> Time { get; } = new();

        public void Dispose()
        {
            Time.Dispose();
            History.Dispose();
        }
    }
}
