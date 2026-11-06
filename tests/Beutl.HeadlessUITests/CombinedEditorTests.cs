using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Composition;
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
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using FluentIcons.Common;
using Moq;
using Reactive.Bindings;
using FluentIcon = FluentIcons.Avalonia.Fluent.FluentIcon;
using TextBlock = Avalonia.Controls.TextBlock;

namespace Beutl.HeadlessUITests;

// Two or three properties that one vector editor shows while they share their timing, and that take a row each otherwise.
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

    private static CombinedEditorViewModel<float, (float, float)> CreateCombined(EngineObject owner)
        => CreateCombined<(float, float)>(owner);

    // The first row of the owner that joins as many float properties as TTuple holds.
    private static CombinedEditorViewModel<float, TTuple> CreateCombined<TTuple>(EngineObject owner)
        where TTuple : struct
    {
        IPropertyEditorContext[] contexts = CreateContexts(owner);
        var combined = contexts.OfType<CombinedEditorViewModel<float, TTuple>>().First();
        foreach (IPropertyEditorContext context in contexts)
        {
            if (!ReferenceEquals(context, combined)) context.Dispose();
        }

        return combined;
    }

    private static Control CreateControl(PropertyEditorViewModel context)
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
        var combined = (CombinedEditorViewModel<float, (float, float)>)scale.Contexts[1];
        Assert.That(combined.EditedProperties.Select(p => p.GetEngineProperty()!.Name), Is.EqualTo(new[] { "ScaleX", "ScaleY" }));
        Assert.That(combined.Header, Is.EqualTo($"{GraphicsStrings.ScaleTransform_ScaleXY} (%)"));

        using var shape = new DisposableContexts(CreateContexts(new RectShape()));
        Assert.That(shape.Contexts[0], Is.TypeOf<CombinedEditorViewModel<float, (float, float)>>());
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

    // A row that names a missing property, mixes value types or cannot animate would silently keep a row for each.
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
                string[] names = attribute.Third is { } third
                    ? [property.Name, attribute.Second, third]
                    : [property.Name, attribute.Second];
                Assert.That(row?.EditedProperties.Select(p => p.GetEngineProperty()!.Name), Is.EqualTo(names), $"{type.Name}.{property.Name}");
            }
        }
    }

    [AvaloniaTest]
    public void The_values_show_with_the_names_of_their_axes()
    {
        var transform = new ScaleTransform(30, 40);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(shape);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(shape);
        combined.Accept(Services(history.History));

        Assert.That(combined.PreviewEdit((-5, 7)), Is.EqualTo((0f, 7f)));
        Assert.That((shape.Width.Value, shape.Height.Value), Is.EqualTo((0f, 7f)));
    }

    [AvaloniaTest]
    public void Reset_returns_both_to_their_defaults_as_one_history_entry()
    {
        var shape = new RectShape { Width = { Value = 5 }, Height = { Value = 6 } };
        using PropertyEditorTestHost.HistoryScope history = new(shape);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(shape);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
            Assert.That(combined.HasAnimation.Value, Is.False, "No keyframes stand for both.");
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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

    [AvaloniaTest]
    public void One_expression_reaches_both_as_one_history_entry()
    {
        var transform = new TranslateTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;

        Assert.That(combined.SetExpression("new Point(Time + 10, 20)", out string? error), Is.True, error);

        var context = new EvaluationContext(MediaTime.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(transform.X.Evaluate(context), Is.EqualTo(12f));
            Assert.That(transform.Y.Evaluate(context), Is.EqualTo(20f));
            Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(IsSeparated(combined), Is.False);
            Assert.That(combined.HasExpression.Value, Is.True);
            Assert.That(combined.CanEdit.Value, Is.False);
            Assert.That(combined.GetExpressionString(), Is.EqualTo("new Point(Time + 10, 20)"));
        });

        history.History.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(transform.X.HasExpression, Is.False);
            Assert.That(transform.Y.HasExpression, Is.False);
            Assert.That(combined.HasExpression.Value, Is.False);
            Assert.That(combined.CanEdit.Value, Is.True);
        });
    }

    [AvaloniaTest]
    public void A_width_and_a_height_take_a_size_and_nothing_else()
    {
        var shape = new RectShape();
        using PropertyEditorTestHost.HistoryScope history = new(shape);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(shape);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;

        foreach (string wrong in new[] { "new Point(1, 2)", "1f", "not an expression" })
        {
            Assert.That(combined.SetExpression(wrong, out string? problem), Is.False, wrong);
            Assert.That(problem, Is.Not.Empty);
        }
        Assert.That(shape.Width.HasExpression, Is.False);
        Assert.That(history.History.UndoCount, Is.EqualTo(before));

        Assert.That(combined.SetExpression("new Size(Sin(Time) * 100, 50)", out string? error), Is.True, error);
        var context = new EvaluationContext(MediaTime.Zero);
        Assert.That(shape.Width.Evaluate(context), Is.EqualTo(0f));
        Assert.That(shape.Height.Evaluate(context), Is.EqualTo(50f));
    }

    [AvaloniaTest]
    public void Removing_the_expression_removes_it_from_both_as_one_history_entry()
    {
        var transform = new TranslateTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        Assert.That(combined.SetExpression("new Point(1, 2)", out string? error), Is.True, error);
        int before = history.History.UndoCount;

        combined.RemoveExpression();

        Assert.Multiple(() =>
        {
            Assert.That(transform.X.HasExpression, Is.False);
            Assert.That(transform.Y.HasExpression, Is.False);
            Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(IsSeparated(combined), Is.False);
        });
    }

    [AvaloniaTest]
    public void An_expression_takes_the_place_of_both_animations_until_undone()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 1));
        transform.Y.SetKeyFrameAnimation(Animation(0, 1));
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();

        Assert.That(combined.SetExpression("new Point(1, 2)", out string? error), Is.True, error);
        Assert.That(transform.X.GetKeyFrameAnimation(), Is.Null);
        Assert.That(transform.Y.GetKeyFrameAnimation(), Is.Null);
        Assert.That(IsSeparated(combined), Is.False);

        history.History.Undo();
        Assert.That(transform.X.GetKeyFrameAnimation(), Is.Not.Null);
        Assert.That(transform.Y.GetKeyFrameAnimation(), Is.Not.Null);
        Assert.That(transform.X.HasExpression, Is.False);
        Assert.That(IsSeparated(combined), Is.False);
    }

    [AvaloniaTest]
    public void The_shared_expression_is_still_shared_after_saving()
    {
        var transform = new TranslateTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using (CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform))
        {
            combined.Accept(Services(history.History));
            Assert.That(combined.SetExpression("var size = 30; new Point(size, size * 2)", out string? error), Is.True, error);
        }

        var copy = (TranslateTransform)CoreSerializer.DeserializeFromJsonObject(
            CoreSerializer.SerializeToJsonObject(transform), typeof(TranslateTransform));

        var context = new EvaluationContext(MediaTime.Zero);
        Assert.That(copy.X.Evaluate(context), Is.EqualTo(30f));
        Assert.That(copy.Y.Evaluate(context), Is.EqualTo(60f));
        using PropertyEditorTestHost.HistoryScope copyHistory = new(copy);
        using CombinedEditorViewModel<float, (float, float)> restored = CreateCombined(copy);
        restored.Accept(Services(copyHistory.History));
        Assert.That(IsSeparated(restored), Is.False);
        Assert.That(restored.GetExpressionString(), Is.EqualTo("var size = 30; new Point(size, size * 2)"));
    }

    // The two rows show the expression of their own type, which stays an edit of that row alone.
    [AvaloniaTest]
    public void A_row_of_its_own_edits_its_value_with_a_plain_expression()
    {
        var transform = new TranslateTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        Assert.That(combined.SetExpression("new Point(1, 2)", out string? error), Is.True, error);
        var window = new Window { Width = 480, Height = 300, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            combined.SeparateComponents();
            HeadlessTestHelpers.Render(3);
            Assert.That(combined.HasExpression.Value, Is.False, "The header's menu does not edit an expression.");
            Assert.That(combined.CanUnseparateComponents.Value, Is.True);

            var row = (PropertyEditorViewModel)window.GetVisualDescendants().OfType<NumberEditor<float>>().First().DataContext!;
            Assert.That(row.GetExpressionString(), Is.EqualTo("(new Point(1, 2)).X"));
            Assert.That(row.SetExpression(row.GetExpressionString()!, out error), Is.True, error);
            HeadlessTestHelpers.Render(3);

            Assert.That(transform.X.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(1f));
            Assert.That(combined.CanUnseparateComponents.Value, Is.False, "The two no longer share an expression.");
        }
        finally { window.Close(); }
    }

    // A script can have statements before what it returns, which a row of its own keeps.
    [AvaloniaTest]
    public void A_row_of_its_own_can_save_a_script_with_statements()
    {
        var transform = new TranslateTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        Assert.That(combined.SetExpression("var size = 30; new Point(size, size * 2)", out string? error), Is.True, error);
        combined.SeparateComponents();

        Assert.That(combined.Components[0].SetExpression(combined.Components[0].GetExpressionString()!, out error), Is.True, error);

        Assert.That(transform.X.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(30f));
    }

    [AvaloniaTest]
    public void The_expression_editor_says_what_to_return_and_writes_both()
    {
        var shape = new RectShape();
        using PropertyEditorTestHost.HistoryScope history = new(shape);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(shape);
        combined.Accept(Services(history.History));
        var window = new Window { Width = 640, Height = 480, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var editor = window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
            Button button = ((Control)editor.MenuContent!).GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            var menu = (FAMenuFlyout)button.ContextFlyout!;
            menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "editExpressionItem")
                .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            menu.Hide();
            HeadlessTestHelpers.Render(3);

            var popup = window.OpenedPopups.Single(popup => popup.Child is ExpressionEditorFlyoutPresenter);
            var presenter = (ExpressionEditorFlyoutPresenter)popup.Child!;
            Assert.That(presenter.ExpressionHint, Does.Contain("new Size("));
            Assert.That(presenter.ExpressionPlaceholder, Does.StartWith("new Size("));
            presenter.GetVisualDescendants().OfType<TextBox>().Single(text => text.Name == "ExpressionTextBox").Text = "new Size(30, 40)";
            presenter.GetVisualDescendants().OfType<Button>().Single(accept => accept.Name == "AcceptButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);

            var context = new EvaluationContext(MediaTime.Zero);
            Assert.Multiple(() =>
            {
                Assert.That(popup.IsOpen, Is.False);
                Assert.That(shape.Width.Evaluate(context), Is.EqualTo(30f));
                Assert.That(shape.Height.Evaluate(context), Is.EqualTo(40f));
                Assert.That(editor.IsEffectivelyVisible, Is.True);
                Assert.That(editor.IsReadOnly, Is.True);
                Assert.That(editor.FirstValue, Is.EqualTo(30f));
                Assert.That(editor.SecondValue, Is.EqualTo(40f));
            });
        }
        finally
        {
            foreach (var popup in window.OpenedPopups.ToArray())
                popup.Close();
            window.Close();
        }
    }

    // The menu fills its icon on a keyframe. Apart, no menu stands for both, and each row's marks its own property's.
    [AvaloniaTest]
    public void Each_menu_marks_the_keyframes_its_row_stands_for()
    {
        var transform = new TranslateTransform();
        transform.X.SetKeyFrameAnimation(Animation(0, 2));
        transform.Y.SetKeyFrameAnimation(Animation(0, 2));
        using var scope = new AnimatedScope(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(scope.History.History, scope.Time));
        var window = new Window { Width = 480, Height = 300, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcons(), Is.EqualTo(new[] { (Icon.Diamond, IconVariant.Filled) }));

            combined.SeparateComponents();
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcons(), Is.EqualTo(new[] { (Icon.Diamond, IconVariant.Filled), (Icon.Diamond, IconVariant.Filled) }));

            combined.UnseparateComponents();
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcons(), Is.EqualTo(new[] { (Icon.Diamond, IconVariant.Filled) }));

            transform.Y.SetKeyFrameAnimation(null);
            HeadlessTestHelpers.Render(3);
            Assert.That(MenuIcons(), Is.EqualTo(new[] { (Icon.Diamond, IconVariant.Filled), (Icon.MoreVertical, IconVariant.Regular) }));
        }
        finally { window.Close(); }

        (Icon, IconVariant)[] MenuIcons()
        {
            PropertyEditorMenu[] menus = [.. window.GetVisualDescendants().OfType<PropertyEditorMenu>().Where(menu => menu.IsEffectivelyVisible)];
            bool separated = menus.Length > 1;
            Assert.That(menus.Select(menu => ReferenceEquals(menu.DataContext, combined)), Is.All.EqualTo(!separated));
            return [.. menus.Select(menu => menu.GetVisualDescendants().OfType<FluentIcon>().Single())
                .Select(icon => (icon.Icon, icon.IconVariant))];
        }
    }

    [AvaloniaTest]
    public void Separating_takes_no_history_entry_and_the_choice_is_saved()
    {
        var transform = new TranslateTransform(1, 2);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;

        combined.SeparateComponents();
        Assert.That(IsSeparated(combined), Is.True);
        Assert.That(history.History.UndoCount, Is.EqualTo(before));

        var json = new JsonObject();
        combined.WriteToJson(json);
        using CombinedEditorViewModel<float, (float, float)> restored = CreateCombined(new TranslateTransform(1, 2));
        Assert.That(IsSeparated(restored), Is.False);
        restored.ReadFromJson(json);
        Assert.That(IsSeparated(restored), Is.True);

        using (CombinedEditorViewModel<float, (float, float)> later = CreateCombined(transform))
        {
            Assert.That(IsSeparated(later), Is.True, "Every editor of the two shares the choice.");
            later.ReadFromJson(new JsonObject { ["KeepsSeparated"] = false });
            Assert.That(IsSeparated(later), Is.True, "A state saved earlier does not undo it.");
        }

        transform.X.SetKeyFrameAnimation(Animation(0, 1));
        HeadlessTestHelpers.Settle();
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
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
                Assert.That(Item(menu, "editExpressionItem").IsVisible, Is.True);
                Assert.That(Item(menu, "copyPropertyPathItem").IsVisible, Is.False, "A path names one property.");
                Assert.That(Item(menu, "copyGetPropertyCodeItem").IsVisible, Is.False);
                Assert.That(SubItem(menu, "copyPropertyPathSubItem").IsVisible, Is.True);
                Assert.That(SubItem(menu, "copyGetPropertyCodeSubItem").IsVisible, Is.True);
                Assert.That(menu.Items.OfType<FAMenuFlyoutSeparator>().Single(i => i.Name == "copyPropertyPathSeparator").IsVisible, Is.True);
                Assert.That(menu.Items.OfType<FAToggleMenuFlyoutItem>().Single(i => i.Name == "uniformEditorToggle").IsVisible, Is.True);
            });
            Item(menu, "separateComponentsItem").RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
            menu.Hide();
            HeadlessTestHelpers.Render(3);

            // Apart, each row has its own property's menu, which also joins the two again.
            PropertyEditorMenu[] rows = [.. window.GetVisualDescendants().OfType<PropertyEditorMenu>().Where(item => item.IsEffectivelyVisible)];
            Assert.That(rows.Select(row => ((PropertyEditorViewModel)row.DataContext!).Header), Is.EqualTo(new[] { "X", "Y" }));
            foreach (PropertyEditorMenu row in rows)
            {
                FAMenuFlyout rowMenu = OpenMenu(row);
                FAMenuFlyoutItem unseparate = Item(rowMenu, "unseparateComponentsItem");
                Assert.Multiple(() =>
                {
                    Assert.That(Item(rowMenu, "separateComponentsItem").IsVisible, Is.False);
                    Assert.That(Item(rowMenu, "editAnimationItem").IsVisible, Is.True);
                    Assert.That(Item(rowMenu, "editExpressionItem").IsVisible, Is.True, "Each row has its own expression.");
                    Assert.That(Item(rowMenu, "copyPropertyPathItem").IsVisible, Is.True, "Each row has its own path.");
                    Assert.That(SubItem(rowMenu, "copyPropertyPathSubItem").IsVisible, Is.False);
                    Assert.That(unseparate.IsVisible, Is.True);
                    Assert.That(unseparate.IsEnabled, Is.True);
                    Assert.That(ToolTip.GetTip(unseparate), Is.EqualTo(Strings.UnseparateComponents_SameTiming));
                });
                rowMenu.Hide();
            }

            // The menu of a row with an expression opens the expression's editor, so this opens the other row's.
            transform.Y.Expression = Expression.Create<float>("1f");
            HeadlessTestHelpers.Render(3);
            FAMenuFlyout xMenu = OpenMenu(rows[0]);
            Assert.That(Item(xMenu, "unseparateComponentsItem").IsEnabled, Is.False);
            xMenu.Hide();
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

    private static FAMenuFlyoutSubItem SubItem(FAMenuFlyout menu, string name)
        => menu.Items.OfType<FAMenuFlyoutSubItem>().Single(item => item.Name == name);

    private static FAMenuFlyoutItem RowMenuItem(PropertyEditor editor, string name)
        => ((FAMenuFlyout)((Control)editor.MenuContent!).GetVisualDescendants().OfType<Button>().First().ContextFlyout!)
            .Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == name);

    // Apart, a width and a height show as the rows of any two properties, with nothing above them, and clicking the menu
    // of either joins them again. That disposes the row whose menu is open.
    [AvaloniaTest]
    [TestCase(0)]
    [TestCase(1)]
    public void The_menu_of_either_row_joins_the_two_again(int clicked)
    {
        var shape = new RectShape();
        using PropertyEditorTestHost.HistoryScope history = new(shape);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(shape);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;
        combined.SeparateComponents();
        var window = new Window { Width = 480, Height = 600, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            NumberEditor<float>[] rows = [.. window.GetVisualDescendants().OfType<NumberEditor<float>>()];
            Assert.That(rows.Select(row => row.Header), Is.EqualTo(new[] { GraphicsStrings.Width, GraphicsStrings.Height }));
            Assert.That(rows.All(row => row.IsEffectivelyVisible), Is.True);
            Assert.That(window.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == combined.Header && text.IsEffectivelyVisible), Is.False, "Nothing names the two above them.");
            Assert.That(window.GetVisualDescendants().OfType<TreeLineDecorator>().Single().IndentLevel, Is.Zero);
            var clickedRow = (PropertyEditorViewModel)rows[clicked].DataContext!;

            Button button = ((Control)rows[clicked].MenuContent!).GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            var menu = (FAMenuFlyout)button.ContextFlyout!;
            FAMenuFlyoutItem unseparate = RowMenuItem(rows[clicked], "unseparateComponentsItem");
            Assert.That(unseparate.IsEffectivelyVisible, Is.True);
            TimelineTestHost.WaitForHitTest(window, unseparate, HeadlessTestHelpers.Center(unseparate, window));
            HeadlessTestHelpers.Click(unseparate, move: true, ticks: 3);

            Assert.That(menu.IsOpen, Is.False);
            Assert.That(IsSeparated(combined), Is.False);
            Assert.That(window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single().IsEffectivelyVisible, Is.True);
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
            Assert.That(clickedRow.IsDisposed, Is.True);
            Assert.That(history.History.UndoCount, Is.EqualTo(before), "Only the view changes.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task The_menu_asks_which_property_to_copy_the_path_and_code_of()
    {
        var transform = new ScaleTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
        combined.Accept(Services(history.History));
        var window = new Window { Width = 480, Height = 400, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var editor = window.GetVisualDescendants().OfType<Vector2Editor<float>>().Single();
            Button button = ((Control)editor.MenuContent!).GetVisualDescendants().OfType<Button>().First();
            Assert.That(await Copied("copyPropertyPathSubItem", 1), Is.EqualTo($"{{{transform.Id.Value}}}.ScaleY"));
            Assert.That(await Copied("copyGetPropertyCodeSubItem", 0), Is.EqualTo($"GetProperty<float>(\"{{{transform.Id.Value}}}.ScaleX\")"));

            async Task<string?> Copied(string subItemName, int index)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                HeadlessTestHelpers.Render(3);
                var menu = (FAMenuFlyout)button.ContextFlyout!;
                FAMenuFlyoutSubItem subItem = SubItem(menu, subItemName);
                FAMenuFlyoutItem[] items = [.. subItem.Items.Cast<FAMenuFlyoutItem>()];
                Assert.That(items.Select(item => item.Text),
                    Is.EqualTo(new[] { GraphicsStrings.ScaleTransform_ScaleX, GraphicsStrings.ScaleTransform_ScaleY }));

                // A submenu opens a moment after the pointer reaches it.
                TopLevel top = TopLevel.GetTopLevel(subItem)!;
                top.MouseMove(HeadlessTestHelpers.Center(subItem, top));
                await Task.Delay(500);
                HeadlessTestHelpers.Render(3);
                Assert.That(TopLevel.GetTopLevel(items[index]), Is.Not.Null, "The submenu opens.");
                HeadlessTestHelpers.Click(items[index], move: true, ticks: 3);
                HeadlessTestHelpers.Settle();
                Assert.That(menu.IsOpen, Is.False);
                return await window.Clipboard!.TryGetTextAsync();
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task The_inline_view_shows_the_two_as_one_layer()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await TestShell.OpenNewSceneEditorAsync($"combined-inline-{Guid.NewGuid():N}");
        var shape = new RectShape();
        await TestShell.AddElementAsync(editor, () => shape, MediaTime.Zero, MediaTime.FromSeconds(2));
        Element element = editor.Scene.Children.Single();
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).Select(element);
        using var tab = new ElementPropertyTabViewModel(editor);
        tab.Element.Value = element;
        var combined = tab.Items[0].Properties.OfType<CombinedEditorViewModel<float, (float, float)>>().First();
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

            Assert.That(shape.Width.GetKeyFrameAnimation(), Is.Not.Null);
            Assert.That(shape.Height.GetKeyFrameAnimation(), Is.Not.Null);
            InlineAnimationLayerViewModel inline = timeline.Inlines.Single();
            Assert.That(inline.Header, Is.EqualTo(Strings.Size));
            Assert.That(inline.Property.Animation, Is.SameAs(combined.GetAnimation()));
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
        var combined = item.Properties.OfType<CombinedEditorViewModel<float, (float, float)>>().First();

        Assert.That(item.FindPropertyEditor(shape, nameof(RectShape.Width)), Is.SameAs(combined));
        Assert.That(item.FindPropertyEditor(shape, nameof(RectShape.Height)), Is.SameAs(combined));
    }

    [AvaloniaTest]
    public void Each_property_keeps_its_own_row_and_host_in_a_node()
    {
        var transform = new TranslateTransform(1, 2);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float)> combined = CreateCombined(transform);
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
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>()
                    .Select(row => RowMenuItem(row, "unseparateComponentsItem").IsVisible), Is.All.False,
                "A row with a port of its own cannot join the other.");
        }
        finally { window.Close(); }
    }

    private static (float, float, float) Rotation(Rotation3DTransform transform)
        => (transform.RotationX.Value, transform.RotationY.Value, transform.RotationZ.Value);

    // Three properties take one row, with a box for each that names the axis of its property.
    [AvaloniaTest]
    public void Three_properties_take_one_row_with_a_box_for_each()
    {
        using var contexts = new DisposableContexts(CreateContexts(new Rotation3DTransform()));
        PropertyEditorViewModel[] rows = [.. contexts.Contexts.Cast<PropertyEditorViewModel>()];
        Assert.That(rows.Select(row => string.Join(" ", row.EditedProperties.Select(p => p.GetEngineProperty()!.Name))),
            Is.EqualTo(new[] { "RotationX RotationY RotationZ", "CenterX CenterY CenterZ", "Depth" }));
        Assert.That(rows.Take(2).Select(row => row.Header), Is.EqualTo(new[] { GraphicsStrings.Rotation, GraphicsStrings.Center }));

        var transform = new Rotation3DTransform(10, 20, 30, 0, 0, 0);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float, float)> combined = CreateCombined<(float, float, float)>(transform);
        combined.Accept(Services(history.History));
        var editor = (Vector3Editor<float>)((StackPanel)CreateControl(combined)).Children[0];

        Assert.Multiple(() =>
        {
            Assert.That(new[] { editor.FirstHeader, editor.SecondHeader, editor.ThirdHeader }, Is.EqualTo(new[] { "X", "Y", "Z" }));
            Assert.That(new[] { editor.FirstValue, editor.SecondValue, editor.ThirdValue }, Is.EqualTo(new[] { 10f, 20f, 30f }));
        });

        transform.RotationZ.Value = 75;
        Assert.That(editor.ThirdValue, Is.EqualTo(75), "An edit made elsewhere shows.");
    }

    [AvaloniaTest]
    public void An_edit_of_three_writes_each_value_as_one_history_entry()
    {
        var transform = new Rotation3DTransform(1, 2, 3, 0, 0, 0);
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float, float)> combined = CreateCombined<(float, float, float)>(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;

        Assert.That(combined.PreviewEdit((10, 20, 30)), Is.EqualTo((10f, 20f, 30f)));
        combined.ConfirmEdit((1, 2, 3), (10, 20, 30));

        Assert.That(Rotation(transform), Is.EqualTo((10f, 20f, 30f)));
        Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
        history.History.Undo();
        Assert.That(Rotation(transform), Is.EqualTo((1f, 2f, 3f)));
    }

    [AvaloniaTest]
    public void Keyframe_commands_run_on_all_three_as_one_history_entry()
    {
        var transform = new Rotation3DTransform(1, 2, 3, 0, 0, 0);
        using var scope = new AnimatedScope(transform);
        using CombinedEditorViewModel<float, (float, float, float)> combined = CreateCombined<(float, float, float)>(transform);
        combined.Accept(Services(scope.History.History, scope.Time));
        scope.History.History.Commit();
        int before = scope.History.History.UndoCount;
        IExpressionProperty<float>[] properties = [transform.RotationX, transform.RotationY, transform.RotationZ];

        combined.PrepareToEditAnimation();
        Assert.Multiple(() =>
        {
            Assert.That(properties.Select(p => p.GetKeyFrameAnimation()!.KeyFrames.Single().Value), Is.EqualTo(new[] { 1f, 2f, 3f }));
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(IsSeparated(combined), Is.False, "The row stays: each has one keyframe at the same time.");
        });

        combined.InsertKeyFrame(MediaTime.FromSeconds(1));
        Assert.Multiple(() =>
        {
            Assert.That(properties.Select(p => p.GetKeyFrameAnimation()!.KeyFrames.Count), Is.All.EqualTo(2));
            Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 2));
            Assert.That(IsSeparated(combined), Is.False);
        });

        combined.RemoveKeyFrame(MediaTime.FromSeconds(1));
        Assert.That(properties.Select(p => p.GetKeyFrameAnimation()!.KeyFrames.Count), Is.All.EqualTo(1));
        Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 3));

        combined.RemoveAnimation();
        Assert.That(properties.Select(p => p.GetKeyFrameAnimation()), Is.All.Null);
        Assert.That(scope.History.History.UndoCount, Is.EqualTo(before + 4));
    }

    // One of the three with keyframes of its own gives each a row, named as its property is, until the three match again.
    [AvaloniaTest]
    public void A_row_for_each_of_three_shows_while_one_differs()
    {
        var transform = new Rotation3DTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float, float)> combined = CreateCombined<(float, float, float)>(transform);
        combined.Accept(Services(history.History));
        var window = new Window { Width = 480, Height = 400, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var editor = window.GetVisualDescendants().OfType<Vector3Editor<float>>().Single();
            Assert.That(editor.IsEffectivelyVisible, Is.True);

            transform.RotationZ.SetKeyFrameAnimation(Animation(0, 1));
            HeadlessTestHelpers.Render(3);

            NumberEditor<float>[] rows = [.. window.GetVisualDescendants().OfType<NumberEditor<float>>()];
            Assert.Multiple(() =>
            {
                Assert.That(editor.IsEffectivelyVisible, Is.False);
                Assert.That(rows.Select(row => row.Header), Is.EqualTo(new[]
                {
                    GraphicsStrings.Rotation3DTransform_RotationX, GraphicsStrings.Rotation3DTransform_RotationY,
                    GraphicsStrings.Rotation3DTransform_RotationZ
                }));
                Assert.That(rows.All(row => row.IsEffectivelyVisible && row.MenuContent != null), Is.True);
                Assert.That(combined.CanUnseparateComponents.Value, Is.False);
            });

            transform.RotationX.SetKeyFrameAnimation(Animation(0, 1));
            transform.RotationY.SetKeyFrameAnimation(Animation(0, 1));
            HeadlessTestHelpers.Render(3);

            Assert.That(editor.IsEffectivelyVisible, Is.True, "The row returns once the three match.");
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
        }
        finally { window.Close(); }
    }

    // The last of the three rows joins them as the first does.
    [AvaloniaTest]
    public void The_menu_of_the_last_of_three_rows_joins_them_again()
    {
        var transform = new Rotation3DTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float, float)> combined = CreateCombined<(float, float, float)>(transform);
        combined.Accept(Services(history.History));
        combined.SeparateComponents();
        var window = new Window { Width = 480, Height = 600, Content = CreateControl(combined) };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            NumberEditor<float>[] rows = [.. window.GetVisualDescendants().OfType<NumberEditor<float>>()];
            Assert.That(rows, Has.Length.EqualTo(3));
            Assert.That(window.GetVisualDescendants().OfType<TextBlock>()
                .Any(text => text.Text == combined.Header && text.IsEffectivelyVisible), Is.False, "Nothing names the three above them.");

            Button button = ((Control)rows[2].MenuContent!).GetVisualDescendants().OfType<Button>().First();
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            HeadlessTestHelpers.Render(3);
            FAMenuFlyoutItem unseparate = RowMenuItem(rows[2], "unseparateComponentsItem");
            Assert.That(unseparate.IsEffectivelyVisible, Is.True);
            TimelineTestHost.WaitForHitTest(window, unseparate, HeadlessTestHelpers.Center(unseparate, window));
            HeadlessTestHelpers.Click(unseparate, move: true, ticks: 3);

            Assert.That(IsSeparated(combined), Is.False);
            Assert.That(window.GetVisualDescendants().OfType<Vector3Editor<float>>().Single().IsEffectivelyVisible, Is.True);
            Assert.That(window.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public void One_expression_reaches_all_three_as_one_history_entry()
    {
        var transform = new Rotation3DTransform();
        using PropertyEditorTestHost.HistoryScope history = new(transform);
        using CombinedEditorViewModel<float, (float, float, float)> combined = CreateCombined<(float, float, float)>(transform);
        combined.Accept(Services(history.History));
        history.History.Commit();
        int before = history.History.UndoCount;
        const string source = "new System.Numerics.Vector3((float)Time + 10, 20, 30)";

        Assert.That(combined.SetExpression("new Point(1, 2)", out string? problem), Is.False, "A point is not a vector.");
        Assert.That(problem, Is.Not.Empty);
        Assert.That(combined.SetExpression(source, out string? error), Is.True, error);

        var context = new EvaluationContext(MediaTime.FromSeconds(2));
        Assert.Multiple(() =>
        {
            Assert.That(transform.RotationX.Evaluate(context), Is.EqualTo(12f));
            Assert.That(transform.RotationY.Evaluate(context), Is.EqualTo(20f));
            Assert.That(transform.RotationZ.Evaluate(context), Is.EqualTo(30f));
            Assert.That(history.History.UndoCount, Is.EqualTo(before + 1));
            Assert.That(IsSeparated(combined), Is.False);
            Assert.That(combined.GetExpressionString(), Is.EqualTo(source));
        });

        Assert.That(combined.SetExpression(combined.ExpressionExample!, out error), Is.True, $"The example works: {error}");
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
