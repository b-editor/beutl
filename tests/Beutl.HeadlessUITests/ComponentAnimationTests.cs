using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using Moq;
using Reactive.Bindings;
using BColor = Beutl.Media.Color;
using GraphScope = Beutl.HeadlessUITests.GraphEditorContextMenuTests.GraphScope;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ComponentAnimationTests
{
    public static IEnumerable<TestCaseData> Values()
    {
        yield return new(new Vector2(1, 2), new Vector2(30, -8));
        yield return new(new Vector3(1, 2, 3), new Vector3(30, -8, 40));
        yield return new(new Vector4(1, 2, 3, 4), new Vector4(30, -8, 40, 9));
        yield return new(new Point(1, 2), new Point(30, -8));
        yield return new(new Size(1, 2), new Size(30, 80));
        yield return new(new Beutl.Graphics.Vector(1, 2), new Beutl.Graphics.Vector(30, -8));
        yield return new(new Rect(1, 2, 3, 4), new Rect(30, -8, 40, 9));
        yield return new(new Thickness(1, 2, 3, 4), new Thickness(30, 8, 40, 9));
        yield return new(new CornerRadius(1, 2, 3, 4), new CornerRadius(30, 8, 40, 9));
        yield return new(new PixelPoint(1, 2), new PixelPoint(30, -8));
        yield return new(new PixelSize(1, 2), new PixelSize(30, 80));
        yield return new(new PixelRect(1, 2, 3, 4), new PixelRect(30, -8, 40, 9));
        yield return new(BColor.FromArgb(40, 0, 100, 255), BColor.FromArgb(255, 255, 0, 100));
        yield return new(new GradingColor(-1, 0, 2), new GradingColor(3, -8, 4));
        yield return new(new RelativePoint(0.1f, 0.2f, RelativeUnit.Relative), new RelativePoint(0.8f, 0.9f, RelativeUnit.Relative));
        yield return new(new RelativePoint(0.1f, 0.2f, RelativeUnit.Relative), new RelativePoint(80, 90, RelativeUnit.Absolute));
        yield return new(new RelativeRect(new Rect(0, 0, 0.2f, 0.3f), RelativeUnit.Relative), new RelativeRect(new Rect(10, 20, 80, 90), RelativeUnit.Absolute));
    }

    [TestCaseSource(nameof(Values))]
    public void Separation_preserves_evaluation_and_serialization<T>(T first, T last)
    {
        var owner = new ComponentTestObject<T>();
        owner.Value.Value = first;
        var animation = new KeyFrameAnimation<T>();
        animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = { Value = MediaTime.FromSeconds(1) }, Value = { Value = first } });
        animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = { Value = MediaTime.FromSeconds(3) }, Value = { Value = last }, Easing = { Value = new SplineEasing(0.2f, 0, 0.8f, 1) } });
        owner.Value.SetKeyFrameAnimation(animation);
        double[] times = [-1, 0, 1, 1.25, 1.75, 2, 2.25, 2.75, 3, 4];
        T[] expected = times.Select(t => owner.Value.Evaluate(new EvaluationContext(MediaTime.FromSeconds(t)))).ToArray();
        var separated = ComponentAnimations.Separate(owner.Value);
        Assert.That(TypeFormat.ToType(TypeFormat.ToString(separated.GetType())), Is.EqualTo(separated.GetType()));
        Assert.That(owner.Value.HasExpression, Is.False);
        Assert.That(separated.Components.All(p => p.GetKeyFrameAnimation()?.KeyFrames.Count == 2), Is.True);
        AssertValues(owner, times, expected);
        object restored = CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(owner), typeof(ComponentTestObject<T>));
        if (restored is IFallback fallback) Assert.Fail(fallback.ErrorMessage ?? "Deserialization returned a fallback.");
        var copy = (ComponentTestObject<T>)restored;
        Assert.That(copy.Value.GetPropertyAnimation(), Is.InstanceOf<IComponentAnimation>());
        AssertValues(copy, times, expected);
    }

    private static void AssertValues<T>(ComponentTestObject<T> owner, double[] times, T[] expected)
    {
        for (int i = 0; i < times.Length; i++)
            Assert.That(owner.Value.Evaluate(new EvaluationContext(MediaTime.FromSeconds(times[i]))), Is.EqualTo(expected[i]), $"at {times[i]}");
    }

    [TestCase("ja")]
    [TestCase("en")]
    public void Component_names_are_localized_without_changing_serialized_names(string language)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            Check(new PixelRect(1, 2, 3, 4), ["X", "Y", "Width", "Height"], ["X", "Y", Strings.Width, Strings.Height]);
            Check(new Thickness(1, 2, 3, 4), ["Left", "Top", "Right", "Bottom"], [Strings.Left, Strings.Top, Strings.Right, Strings.Bottom]);
            Check(new CornerRadius(1, 2, 3, 4), ["TopLeft", "TopRight", "BottomRight", "BottomLeft"],
                [Strings.TopLeft, Strings.TopRight, Strings.BottomRight, Strings.BottomLeft]);
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }

        static void Check<T>(T value, string[] names, string[] labels)
        {
            var owner = new ComponentTestObject<T>();
            owner.Value.Value = value;
            ComponentAnimations.Separate(owner.Value);
            var copy = (ComponentTestObject<T>)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(owner), typeof(ComponentTestObject<T>));
            foreach (var item in new[] { owner, copy })
            {
                var split = (IComponentAnimation)item.Value.GetPropertyAnimation()!;
                Assert.That(split.Components.Select(p => p.Name), Is.EqualTo(names));
                Assert.That(split.Components.Select(p => PropertyAdapterFactory.CreateAdapter(p, (CoreObject)split).DisplayName), Is.EqualTo(labels));
            }
        }
    }

    [AvaloniaTest]
    public void Independent_keys_and_history_preserve_other_components()
    {
        var owner = new ComponentTestObject<Vector2>();
        owner.Value.Value = new Vector2(10, 20);
        using var history = new PropertyEditorTestHost.HistoryScope(owner);
        using var vm = VectorEditors.Vector2(new AnimatablePropertyAdapter<Vector2>(owner.Value, owner));
        vm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        vm.SeparateComponents();
        var separated = (IComponentAnimation<Vector2>)owner.Value.GetPropertyAnimation()!;
        var x = (AnimatableProperty<float>)separated.Components[0];
        using var xVm = new ValueEditorViewModel<float>(new AnimatablePropertyAdapter<float>(x, (CoreObject)separated));
        xVm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        xVm.PrepareToEditAnimation();
        var animation = x.GetKeyFrameAnimation()!;
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = 50 } });
        history.History.Commit("Edit X");
        Assert.That(separated.EvaluateAt(MediaTime.FromSeconds(1)), Is.EqualTo(new Vector2(30, 20)));
        Assert.That(separated.Components[1].GetKeyFrameAnimation(), Is.Null);
        history.History.Undo();
        Assert.That(animation.KeyFrames.Count, Is.EqualTo(1));
        history.History.Undo();
        Assert.That(x.GetKeyFrameAnimation(), Is.Null);
        history.History.Undo();
        Assert.That(owner.Value.GetPropertyAnimation(), Is.Null);
        history.History.Redo();
        history.History.Redo();
        history.History.Redo();
        Assert.That(separated.EvaluateAt(MediaTime.FromSeconds(1)), Is.EqualTo(new Vector2(30, 20)));
    }

    [Test]
    public void Color_picker_changes_only_changed_channels_and_tracks_follow_owner_time()
    {
        var owner = new ComponentTestObject<BColor>();
        ((IModifiableHierarchical)new ComponentTestRoot()).AddChild(owner);
        owner.IsTimeAnchor.Value = true;
        owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(10));
        owner.Value.Value = BColor.FromArgb(255, 20, 40, 60);
        var separated = (IComponentAnimation<BColor>)ComponentAnimations.Separate(owner.Value);
        var r = (AnimatableProperty<byte>)separated.Components[0];
        var animation = new KeyFrameAnimation<byte>();
        animation.KeyFrames.Add(new KeyFrame<byte> { Value = { Value = 0 } });
        animation.KeyFrames.Add(new KeyFrame<byte> { Value = { Value = 255 }, KeyTime = { Value = MediaTime.FromSeconds(2) } });
        r.SetKeyFrameAnimation(animation);
        Assert.That(animation.ToScene(MediaTime.Zero), Is.EqualTo(MediaTime.FromSeconds(5)));
        Assert.That(separated.EvaluateAt(MediaTime.FromSeconds(6)).R, Is.EqualTo(188));
        separated.SetValue(BColor.FromArgb(255, 255, 90, 60), MediaTime.FromSeconds(6));
        Assert.That(animation.KeyFrames.Select(k => k.Value), Is.EqualTo(new object[] { (byte)0, (byte)255 }));
        Assert.That(separated.EvaluateAt(MediaTime.FromSeconds(6)).G, Is.EqualTo(90));
        owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(8), MediaTime.FromSeconds(10));
        Assert.That(separated.EvaluateAt(MediaTime.FromSeconds(9)).R, Is.EqualTo(188));
        animation.UseGlobalClock.Value = true;
        Assert.That(separated.EvaluateAt(MediaTime.FromSeconds(1)).R, Is.EqualTo(188));
    }

    [Test]
    public void Canvas_offsets_preserve_independent_times_and_other_components()
    {
        var owner = new ComponentTestObject<Vector2>();
        var animation = new KeyFrameAnimation<Vector2>();
        animation.KeyFrames.Add(new KeyFrame<Vector2> { Value = { Value = new Vector2(10, 20) } });
        animation.KeyFrames.Add(new KeyFrame<Vector2> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = new Vector2(30, 60) } });
        owner.Value.SetKeyFrameAnimation(animation);
        var split = (IComponentAnimation<Vector2>)ComponentAnimations.Separate(owner.Value);
        var x = split.Components[0].GetKeyFrameAnimation()!;
        var y = split.Components[1].GetKeyFrameAnimation()!;
        y.KeyFrames[1].KeyTime.Value = MediaTime.FromSeconds(4);
        split.TransformValues(v => v + new Vector2(5, 0), MediaTime.FromSeconds(1));
        Assert.That(split.EvaluateAt(MediaTime.FromSeconds(1)), Is.EqualTo(new Vector2(25, 30)));
        Assert.That(x.KeyFrames.Select(k => k.Value), Is.EqualTo(new object[] { 15f, 35f }));
        Assert.That(y.KeyFrames.Select(k => k.Value), Is.EqualTo(new object[] { 20f, 60f }));
        Assert.That(y.KeyFrames[1].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
    }

    [Test]
    public void Component_values_and_keys_use_the_original_property_bounds()
    {
        var owner = new ComponentTestObject<Point>();
        owner.Value.SetValidator(owner.Value.CreateValidator([new System.ComponentModel.DataAnnotations.RangeAttribute(typeof(Point), "0, 0", "100, 100")]));
        owner.Value.Value = new Point(30, 40);
        Assert.That(owner.Value.Value, Is.EqualTo(new Point(30, 40)));
        var split = (IComponentAnimation<Point>)ComponentAnimations.Separate(owner.Value);
        var x = (IProperty<float>)split.Components[0];
        x.Value = 300;
        Assert.That(x.Value, Is.EqualTo(100));
        var animation = new KeyFrameAnimation<float>();
        var key = new KeyFrame<float>();
        animation.KeyFrames.Add(key);
        x.SetKeyFrameAnimation(animation);
        key.Value.Value = -20;
        Assert.That(key.Value.Value, Is.Zero);
        Assert.That(split.EvaluateAt(MediaTime.Zero), Is.EqualTo(new Point(0, 40)));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Reset_uses_original_defaults_for_components_and_the_combined_editor(bool roundTrip)
    {
        var brush = new SolidColorBrush();
        var split = ComponentAnimations.Separate(brush.Color);
        split.Components.Single(p => p.Name == "R").Value = (byte)32;
        split.Components.Single(p => p.Name == "A").Value = (byte)64;
        if (roundTrip)
        {
            brush = (SolidColorBrush)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(brush), typeof(SolidColorBrush));
            split = (IComponentAnimation)brush.Color.GetPropertyAnimation()!;
        }

        using var history = new PropertyEditorTestHost.HistoryScope(brush);
        var alpha = split.Components.Single(p => p.Name == "A");
        using var alphaVm = new ValueEditorViewModel<byte>((IPropertyAdapter<byte>)PropertyAdapterFactory.CreateAdapter(alpha, (CoreObject)split));
        using var combined = new ValueEditorViewModel<BColor>((IPropertyAdapter<BColor>)PropertyAdapterFactory.CreateAdapter(brush.Color, brush));
        alphaVm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        combined.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
        Assert.Multiple(() =>
        {
            alphaVm.Reset();
            Assert.That(alpha.Value, Is.EqualTo((byte)255));
            history.History.Undo();
            Assert.That(alpha.Value, Is.EqualTo((byte)64));
            combined.Reset();
            Assert.That(combined.Value.Value, Is.EqualTo(Colors.White));
            history.History.Undo();
            Assert.That(combined.Value.Value, Is.EqualTo(BColor.FromArgb(64, 32, 255, 255)));
        });
    }

    [AvaloniaTest]
    [TestCase(5, 10, 20)]
    [TestCase(6, 30, 40)]
    public async Task Separating_at_an_offset_refreshes_the_combined_editor(int time, float x, float y)
    {
        await TestReset.ResetShellAsync();
        using var scope = await GraphScope.CreateAsync();
        var owner = new ComponentTestObject<Vector2>();
        scope.Model.Element!.Objects.Add(owner);
        owner.IsTimeAnchor.Value = true;
        owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(10));
        var animation = new KeyFrameAnimation<Vector2>();
        for (int i = 0; i < 3; i++)
            animation.KeyFrames.Add(new KeyFrame<Vector2>
            {
                KeyTime = { Value = MediaTime.FromSeconds(i * 2) },
                Value = { Value = new Vector2(10 + i * 20, 20 + i * 20) }
            });
        owner.Value.SetKeyFrameAnimation(animation);
        var clock = (IEditorClock)scope.Model.EditorContext.GetService(typeof(IEditorClock))!;
        clock.Seek(MediaTime.FromSeconds(time));
        using var vm = new ValueEditorViewModel<Vector2>(new AnimatablePropertyAdapter<Vector2>(owner.Value, owner));
        vm.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(IEditorClock), clock), (typeof(HistoryManager), scope.Model.HistoryManager)));
        vm.SeparateComponents();
        Assert.That(vm.Value.Value, Is.EqualTo(new Vector2(x, y)));
        vm.SetValue(vm.Value.Value with { X = 75 });
        var split = (IComponentAnimation<Vector2>)owner.Value.GetPropertyAnimation()!;
        Assert.That(split.GetEditingValue(clock.CurrentTime.Value), Is.EqualTo(new Vector2(75, y)));
        Assert.That(split.Components[1].GetKeyFrameAnimation()!.KeyFrames.Select(key => key.Value),
            Is.EqualTo(new object[] { 20f, 40f, 60f }));
    }

    [AvaloniaTest]
    public async Task Unseparating_freezes_the_current_evaluation_and_undo_restores_tracks_and_expressions()
    {
        await TestReset.ResetShellAsync();
        using var scope = await GraphScope.CreateAsync();
        var owner = new ComponentTestObject<Vector3>();
        scope.Model.Element!.Objects.Add(owner);
        owner.IsTimeAnchor.Value = true;
        owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(10));
        owner.Value.Value = new Vector3(10, 20, 30);
        owner.Value.SetKeyFrameAnimation(new KeyFrameAnimation<Vector3>
        {
            KeyFrames =
            {
                new KeyFrame<Vector3> { Value = { Value = new Vector3(10, 20, 30) } },
                new KeyFrame<Vector3> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = new Vector3(30, 60, 70) } }
            }
        });
        var split = ComponentAnimations.Separate(owner.Value);
        var y = split.Components[1].GetKeyFrameAnimation()!;
        y.KeyFrames[1].KeyTime.Value = MediaTime.FromSeconds(4);
        ((IExpressionProperty<float>)split.Components[2]).Expression = Expression.Create<float>("75");
        scope.Model.HistoryManager.Commit();
        var clock = (IEditorClock)scope.Model.EditorContext.GetService(typeof(IEditorClock))!;
        clock.Seek(MediaTime.FromSeconds(6));
        using var vm = new ValueEditorViewModel<Vector3>(new AnimatablePropertyAdapter<Vector3>(owner.Value, owner));
        vm.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(IEditorClock), clock), (typeof(HistoryManager), scope.Model.HistoryManager)));
        var expected = new Vector3(20, 30, 75);
        Assert.That(owner.Value.Evaluate(new EvaluationContext(clock.CurrentTime.Value)), Is.EqualTo(expected));
        Assert.That(vm.Value.Value, Is.EqualTo(new Vector3(30, 60, 75)), "the editable endpoints differ from the rendered value");

        vm.UnseparateComponents();
        Assert.That(owner.Value.GetPropertyAnimation(), Is.Null);
        Assert.That(owner.Value.Value, Is.EqualTo(expected));
        Assert.That(vm.Value.Value, Is.EqualTo(expected));
        clock.Seek(MediaTime.FromSeconds(8));
        Assert.That(owner.Value.Evaluate(new EvaluationContext(clock.CurrentTime.Value)), Is.EqualTo(expected));

        scope.Model.HistoryManager.Undo();
        Assert.That(owner.Value.GetPropertyAnimation(), Is.SameAs(split));
        Assert.That(owner.Value.Value, Is.EqualTo(new Vector3(10, 20, 30)));
        Assert.That(y.KeyFrames[1].KeyTime.Value, Is.EqualTo(MediaTime.FromSeconds(4)));
        Assert.That(((IExpressionProperty<float>)split.Components[2]).HasExpression, Is.True);
        Assert.That(owner.Value.Evaluate(new EvaluationContext(clock.CurrentTime.Value)), Is.EqualTo(new Vector3(30, 50, 75)));
        scope.Model.HistoryManager.Redo();
        Assert.That(owner.Value.GetPropertyAnimation(), Is.Null);
        Assert.That(vm.Value.Value, Is.EqualTo(expected));
        var copy = (ComponentTestObject<Vector3>)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(owner), typeof(ComponentTestObject<Vector3>));
        Assert.That(copy.Value.GetPropertyAnimation(), Is.Null);
        Assert.That(copy.Value.Evaluate(new EvaluationContext(clock.CurrentTime.Value)), Is.EqualTo(expected));

        vm.SeparateComponents();
        var separatedAgain = (IComponentAnimation)owner.Value.GetPropertyAnimation()!;
        Assert.That(separatedAgain.Components.All(p => p.GetPropertyAnimation() == null && !((IExpressionProperty)p).HasExpression), Is.True);
        Assert.That(owner.Value.Evaluate(new EvaluationContext(clock.CurrentTime.Value)), Is.EqualTo(expected));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Combined_color_evaluates_component_expressions_and_keeps_editable_key_endpoints(bool roundTrip)
    {
        var brush = new SolidColorBrush();
        var split = ComponentAnimations.Separate(brush.Color);
        var red = (AnimatableProperty<byte>)split.Components.Single(p => p.Name == "R");
        red.SetKeyFrameAnimation(new KeyFrameAnimation<byte>
        {
            KeyFrames =
            {
                new KeyFrame<byte> { Value = { Value = 100 } },
                new KeyFrame<byte> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = 200 } }
            }
        });
        using var vm = new ValueEditorViewModel<BColor>((IPropertyAdapter<BColor>)PropertyAdapterFactory.CreateAdapter(brush.Color, brush));
        red.Expression = Expression.Create<byte>("(byte)(Time * 10)");
        Assert.That(vm.Value.Value, Is.EqualTo(Colors.Cyan), "an expression edit refreshes the existing picker");
        if (roundTrip)
            brush = (SolidColorBrush)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(brush), typeof(SolidColorBrush));
        split = (IComponentAnimation)brush.Color.GetPropertyAnimation()!;
        var components = (IComponentAnimation<BColor>)split;
        using var reopened = new ValueEditorViewModel<BColor>((IPropertyAdapter<BColor>)PropertyAdapterFactory.CreateAdapter(brush.Color, brush));
        Assert.That(reopened.Value.Value, Is.EqualTo(Colors.Cyan));
        Assert.That(components.GetEditingValue(MediaTime.FromSeconds(1)).R, Is.EqualTo(10));
        ((IExpressionProperty<byte>)split.Components.Single(p => p.Name == "R")).Expression = null;
        Assert.That(components.GetEditingValue(MediaTime.FromSeconds(1)).R, Is.EqualTo(200), "ordinary keys use the editable endpoint");
    }

    [AvaloniaTest]
    public async Task Combined_editor_tracks_owner_moves_without_seeking()
    {
        await TestReset.ResetShellAsync();
        using var scope = await GraphScope.CreateAsync();
        var owner = new ComponentTestObject<Vector2>();
        var animation = new KeyFrameAnimation<Vector2>();
        animation.KeyFrames.Add(new KeyFrame<Vector2> { Value = { Value = new Vector2(10, 20) } });
        animation.KeyFrames.Add(new KeyFrame<Vector2> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = new Vector2(30, 40) } });
        owner.Value.SetKeyFrameAnimation(animation);
        scope.Model.Element!.Objects.Add(owner);
        owner.IsTimeAnchor.Value = true;
        owner.TimeRange.Value = new TimeRange(MediaTime.Zero, MediaTime.FromSeconds(10));
        ComponentAnimations.Separate(owner.Value);
        var editor = (Beutl.ViewModels.EditViewModel)scope.Model.EditorContext;
        var clock = (IEditorClock)editor.GetService(typeof(IEditorClock))!;
        using var vm = new ValueEditorViewModel<Vector2>(new AnimatablePropertyAdapter<Vector2>(owner.Value, owner));
        vm.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(IEditorClock), clock)));
        clock.Seek(MediaTime.FromSeconds(1));
        Assert.That(vm.Value.Value, Is.EqualTo(new Vector2(30, 40)));
        owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(2), MediaTime.FromSeconds(10));
        Assert.That(vm.Value.Value, Is.EqualTo(new Vector2(10, 20)));
        Assert.That(clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
    }

    [AvaloniaTest]
    public async Task Graph_and_inline_timeline_find_independent_tracks_and_restore_selection()
    {
        await TestReset.ResetShellAsync();
        using var scope = await GraphScope.CreateAsync();
        var owner = new ComponentTestObject<Vector3>();
        owner.Value.Value = new Vector3(10, 20, 30);
        Element element = scope.Model.Element!;
        element.Objects.Add(owner);
        var split = ComponentAnimations.Separate(owner.Value);
        scope.Model.HistoryManager.Commit();
        using var graph = new GraphEditorTabViewModel(scope.Model.EditorContext);
        graph.Element.Value = element;
        var parent = graph.TreeItems.Single(x => x.Children.Any(c => ReferenceEquals(c.Property, owner.Value)))
            .Children.Single(c => ReferenceEquals(c.Property, owner.Value));
        Assert.That(parent.Children.Select(c => c.Name.Value), Is.EqualTo(new[] { "X", "Y", "Z" }));
        graph.EnableAnimation(parent.Children[0]);
        var x = split.Components[0];
        var animation = x.GetKeyFrameAnimation()!;
        Assert.That(animation.KeyFrames.Count, Is.EqualTo(1));
        Assert.That(split.Components[1].GetKeyFrameAnimation(), Is.Null);
        Assert.That(graph.Items.Any(item => ReferenceEquals(item.Object, animation)), Is.True);

        using var timeline = new TimelineTabViewModel(scope.Model.EditorContext);
        timeline.AttachInline((IAnimatablePropertyAdapter)PropertyAdapterFactory.CreateAdapter(x, (CoreObject)split), element);
        var state = new JsonObject();
        timeline.WriteToJson(state);
        using var restored = new TimelineTabViewModel(scope.Model.EditorContext);
        restored.ReadFromJson(state);
        Assert.That(restored.Inlines.Select(i => i.Property.Animation), Does.Contain(animation));
        graph.RemoveAnimation(parent.Children[0]);
        Assert.That(x.GetKeyFrameAnimation(), Is.Null);
        Assert.That(owner.Value.GetPropertyAnimation(), Is.SameAs(split));
    }

    [Test]
    public void Separation_preserves_expression_chain_and_fixed_components()
    {
        var owner = new ComponentTestObject<Vector2>();
        owner.Value.Value = new Vector2(10, 20);
        owner.Value.Expression = Expression.Create<Vector2>("new System.Numerics.Vector2(7, 9)");
        var split = ComponentAnimations.Separate(owner.Value);
        Assert.That(owner.Value.HasExpression, Is.True);
        Assert.That(owner.Value.Expression!.Replaced, Is.SameAs(split));
        var copy = (ComponentTestObject<Vector2>)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(owner), typeof(ComponentTestObject<Vector2>));
        Assert.That(copy.Value.Expression!.Replaced, Is.InstanceOf<IComponentAnimation>());
        Assert.That(copy.Value.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(new Vector2(7, 9)));
        copy.Value.Expression = null;
        Assert.That(copy.Value.HasExpression, Is.False);
        Assert.That(copy.Value.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(new Vector2(10, 20)));
    }

    [Test]
    public void Component_expressions_share_the_owners_evaluation_context()
    {
        var owner = new ComponentTestObject<Vector2>();
        owner.Value.Value = new Vector2(10, 20);
        var split = ComponentAnimations.Separate(owner.Value);
        ((IExpressionProperty<float>)split.Components[0]).Expression = new OwnerReference(owner.Value);
        Assert.That(owner.Value.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(new Vector2(0, 20)));
    }

    private sealed class OwnerReference(IExpressionProperty<Vector2> owner) : IExpression<float>
    {
        public string ExpressionString => "owner.X";
        public bool Validate(out string? error) { error = null; return true; }
        public float Evaluate(ExpressionContext context)
        {
            Assert.That(context.IsEvaluating(owner), Is.True, "The aggregate must stay on the expression stack.");
            return owner.Evaluate(context).X;
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Component_expression_timing_matches_rendering_in_both_editors(bool roundTrip)
    {
        var owner = new ComponentTestObject<Vector4>();
        owner.IsTimeAnchor.Value = true;
        owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(10));
        var split = (IComponentAnimation<Vector4>)ComponentAnimations.Separate(owner.Value);
        string[] expressions = ["(float)Time", "(float)Start", "(float)Duration", "(float)Progress"];
        for (int i = 0; i < expressions.Length; i++)
            ((IExpressionProperty<float>)split.Components[i]).Expression = Expression.Create<float>(expressions[i]);
        if (roundTrip)
            owner = (ComponentTestObject<Vector4>)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(owner), typeof(ComponentTestObject<Vector4>));
        ((IModifiableHierarchical)new ComponentTestRoot()).AddChild(owner);
        split = (IComponentAnimation<Vector4>)owner.Value.GetPropertyAnimation()!;
        using var time = new ReactivePropertySlim<MediaTime>(MediaTime.FromSeconds(5));
        var clock = new Mock<IEditorClock>();
        clock.SetupGet(value => value.CurrentTime).Returns(time);
        var services = new PropertyEditorTestHost.ServiceVisitor((typeof(IEditorClock), clock.Object));
        using var combined = new ValueEditorViewModel<Vector4>(new AnimatablePropertyAdapter<Vector4>(owner.Value, owner));
        combined.Accept(services);
        var components = split.Components.Select(property => new ValueEditorViewModel<float>(
            (IPropertyAdapter<float>)PropertyAdapterFactory.CreateAdapter(property, (CoreObject)split))).ToArray();
        try
        {
            foreach (var component in components) component.Accept(services);
            Check(new Vector4(0, 5, 10, 0));
            time.Value = MediaTime.FromSeconds(7);
            Check(new Vector4(2, 5, 10, 0.2f));
            owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(6), MediaTime.FromSeconds(20));
            Check(new Vector4(1, 6, 20, 0.05f));
            time.Value = MediaTime.FromSeconds(8);
            Check(new Vector4(2, 6, 20, 0.1f));
        }
        finally
        {
            foreach (var component in components) component.Dispose();
        }

        void Check(Vector4 expected)
        {
            Assert.Multiple(() =>
            {
                Assert.That(owner.Value.Evaluate(new EvaluationContext(time.Value)), Is.EqualTo(expected), "rendering");
                Assert.That(split.EvaluateAt(time.Value), Is.EqualTo(expected), "canvas editing");
                Assert.That(split.GetEditingValue(time.Value), Is.EqualTo(expected), "component editing");
                Assert.That(combined.Value.Value, Is.EqualTo(expected), "combined editor");
                Assert.That(components.Select(component => component.Value.Value),
                    Is.EqualTo(new[] { expected.X, expected.Y, expected.Z, expected.W }), "scalar editors");
            });
        }
    }

    [AvaloniaTest]
    [TestCase(false, "ja")]
    [TestCase(true, "en")]
    public void Separated_relative_units_can_change_without_losing_tracks(bool roundTrip, string language)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            Check(new RelativePoint(0.25f, 0.5f, RelativeUnit.Relative), new RelativePoint(20, 30, RelativeUnit.Absolute),
                value => value.Unit, (value, unit) => new RelativePoint(value.Point, unit));
            Check(new RelativeRect(new Rect(0.25f, 0.5f, 0.75f, 1), RelativeUnit.Relative),
                new RelativeRect(new Rect(20, 30, 40, 50), RelativeUnit.Absolute),
                value => value.Unit, (value, unit) => new RelativeRect(value.Rect, unit));
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }

        void Check<T>(T first, T last, Func<T, RelativeUnit> getUnit, Func<T, RelativeUnit, T> withUnit)
        {
            var owner = new ComponentTestObject<T>();
            owner.Value.Value = first;
            owner.IsTimeAnchor.Value = true;
            owner.TimeRange.Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(10));
            var animation = new KeyFrameAnimation<T>();
            animation.KeyFrames.Add(new KeyFrame<T> { Value = { Value = first } });
            animation.KeyFrames.Add(new KeyFrame<T> { KeyTime = { Value = MediaTime.FromSeconds(2) }, Value = { Value = last } });
            owner.Value.SetKeyFrameAnimation(animation);
            var split = (ComponentAnimation<T, float>)ComponentAnimations.Separate(owner.Value);
            ((IExpressionProperty<float>)split.Components[0]).Expression = Expression.Create<float>("0.25f");
            if (roundTrip)
                owner = (ComponentTestObject<T>)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(owner), typeof(ComponentTestObject<T>));
            ((IModifiableHierarchical)new ComponentTestRoot()).AddChild(owner);
            split = (ComponentAnimation<T, float>)owner.Value.GetPropertyAnimation()!;
            var unitSource = split.UnitSource.Value;
            string tracks = TrackSnapshot();
            using var time = new ReactivePropertySlim<MediaTime>(MediaTime.FromSeconds(5));
            var clock = new Mock<IEditorClock>();
            clock.SetupGet(value => value.CurrentTime).Returns(time);
            using var history = new PropertyEditorTestHost.HistoryScope(owner);
            var (vm, control) = PropertyEditorTestHost.CreateControl(new AnimatablePropertyAdapter<T>(owner.Value, owner));
            using (vm)
            {
                vm.Accept(new PropertyEditorTestHost.ServiceVisitor((typeof(IEditorClock), clock.Object), (typeof(HistoryManager), history.History)));
                var window = new Window
                {
                    Width = language == "ja" ? 280 : 480,
                    Height = 480,
                    Content = control,
                    RequestedThemeVariant = language == "ja" ? ThemeVariant.Dark : ThemeVariant.Light
                };
                try
                {
                    window.Show();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(control.GetVisualDescendants().OfType<PropertyEditor>()
                        .Where(editor => editor is RelativePointEditor or RelativeRectEditor).All(editor => !editor.IsEffectivelyVisible), Is.True);
                    var unitEditor = control.GetVisualDescendants().OfType<EnumEditor>().Single(editor => editor.IsEffectivelyVisible);
                    var combo = unitEditor.GetVisualDescendants().OfType<ComboBox>().Single();
                    Assert.That(combo.SelectedIndex, Is.EqualTo((int)RelativeUnit.Relative));
                    time.Value = MediaTime.FromSeconds(7);
                    Assert.That(combo.SelectedIndex, Is.EqualTo((int)RelativeUnit.Absolute));
                    time.Value = MediaTime.FromSeconds(5);
                    Assert.That(combo.SelectedIndex, Is.EqualTo((int)RelativeUnit.Relative));
                    Assert.That(split.UnitSource.Value, Is.SameAs(unitSource), "displaying or seeking must not replace the inherited unit schedule");

                    T evaluated = owner.Value.Evaluate(new EvaluationContext(time.Value));
                    combo.SelectedIndex = (int)RelativeUnit.Absolute;
                    HeadlessTestHelpers.Render(3);
                    Assert.That(owner.Value.Evaluate(new EvaluationContext(time.Value)), Is.EqualTo(withUnit(evaluated, RelativeUnit.Absolute)));
                    Assert.That(split.UnitSource.Value, Is.Null);
                    Assert.That(TrackSnapshot(), Is.EqualTo(tracks));
                    history.History.Undo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(split.UnitSource.Value, Is.SameAs(unitSource));
                    Assert.That(combo.SelectedIndex, Is.EqualTo((int)RelativeUnit.Relative));
                    history.History.Redo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(split.UnitSource.Value, Is.Null);
                    Assert.That(combo.SelectedIndex, Is.EqualTo((int)RelativeUnit.Absolute));
                    var copy = (ComponentTestObject<T>)CoreSerializer.DeserializeFromJsonObject(CoreSerializer.SerializeToJsonObject(owner), typeof(ComponentTestObject<T>));
                    Assert.That(getUnit(copy.Value.Evaluate(new EvaluationContext(time.Value))), Is.EqualTo(RelativeUnit.Absolute));
                    combo.SelectedIndex = (int)RelativeUnit.Relative;
                    HeadlessTestHelpers.Render(3);
                    Assert.That(owner.Value.Evaluate(new EvaluationContext(time.Value)), Is.EqualTo(evaluated));
                    Assert.That(TrackSnapshot(), Is.EqualTo(tracks));
                    if (Environment.GetEnvironmentVariable("BEUTL_COMPONENT_CAPTURE") is { } directory)
                    {
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(System.IO.Path.Combine(directory, $"components-{typeof(T).Name}-{language}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                        combo.IsDropDownOpen = true;
                        HeadlessTestHelpers.Render(3);
                        using var popup = TopLevel.GetTopLevel(window.OpenedPopups.Last().Child!)!.CaptureRenderedFrame();
                        popup!.Save(System.IO.Path.Combine(directory, $"components-{typeof(T).Name}-units-{language}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                }
                finally { window.Close(); }
            }

            string TrackSnapshot()
            {
                JsonObject json = CoreSerializer.SerializeToJsonObject(split);
                json.Remove(nameof(split.BaseValue));
                json.Remove(nameof(split.UnitSource));
                return json.ToJsonString();
            }
        }
    }

    [AvaloniaTest]
    [TestCase("ja")]
    [TestCase("en")]
    public void Separated_color_opens_with_localized_channels_without_the_combined_picker(string language)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            var brush = new SolidColorBrush();
            ComponentAnimations.Separate(brush.Color);
            var (vm, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(brush.Color, brush));
            using (vm)
            {
                var window = new Window
                {
                    Width = 280,
                    Height = 360,
                    Content = control,
                    RequestedThemeVariant = language == "ja" ? ThemeVariant.Light : ThemeVariant.Dark
                };
                try
                {
                    window.Show();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(control.GetVisualDescendants().OfType<ColorEditor>().Single().IsEffectivelyVisible, Is.False);
                    var split = (IComponentAnimation)brush.Color.GetPropertyAnimation()!;
                    Assert.That(split.Components.Select(p => p.Name), Is.EqualTo(new[] { "R", "G", "B", "A" }));
                    Assert.That(control.GetVisualDescendants().OfType<NumberEditor<byte>>()
                        .Where(editor => editor.IsEffectivelyVisible).Select(editor => editor.Header),
                        Is.EqualTo(language == "ja" ? new[] { "赤", "緑", "青", "不透明度" } : ["Red", "Green", "Blue", "Opacity"]));
                    var parentMenu = control.GetVisualDescendants().OfType<PropertyEditorMenu>()
                        .Single(item => ReferenceEquals(item.DataContext, vm) && item.IsEffectivelyVisible);
                    var parentButton = parentMenu.GetVisualDescendants().OfType<Button>().First();
                    parentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    HeadlessTestHelpers.Render(3);
                    var parentFlyout = (FAMenuFlyout)parentButton.ContextFlyout!;
                    AssertComponentMenu(parentFlyout, separated: true);
                    parentFlyout.Hide();
                    if (Environment.GetEnvironmentVariable("BEUTL_COMPONENT_CAPTURE") is { } directory)
                    {
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(System.IO.Path.Combine(directory, $"components-color-{language}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                }
                finally { window.Close(); }
            }
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [AvaloniaTest]
    [TestCase(280, "ja")]
    [TestCase(480, "en")]
    public void Component_editors_render_with_individual_keyframe_menus(int width, string language)
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            var owner = new ComponentTestObject<Vector3>();
            owner.Value.Value = new Vector3(10, 20, 30);
            using var history = new PropertyEditorTestHost.HistoryScope(owner);
            owner.Value.SetAttributes("Value", [new DisplayAttribute { Name = language == "ja" ? "位置" : "Position" }]);
            var (vm, control) = PropertyEditorTestHost.CreateControl(new AnimatablePropertyAdapter<Vector3>(owner.Value, owner));
            using (vm)
            {
                vm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
                var window = new Window
                {
                    Width = width,
                    Height = 360,
                    Content = control,
                    RequestedThemeVariant = language == "ja" ? ThemeVariant.Dark : ThemeVariant.Light
                };
                try
                {
                    window.Show();
                    HeadlessTestHelpers.Render(3);
                    var combined = control.GetVisualDescendants().OfType<Vector3Editor<float>>().Single();
                    Assert.That(combined.IsEffectivelyVisible, Is.True);
                    var button = ((Control)combined.MenuContent!).GetVisualDescendants().OfType<Button>().First();
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var menu = (FAMenuFlyout)button.ContextFlyout!;
                    HeadlessTestHelpers.Render(3);
                    AssertComponentMenu(menu, separated: false, uniformVisible: true);
                    menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "separateComponentsItem")
                        .RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
                    menu.Hide();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(combined.IsEffectivelyVisible, Is.False);
                    Assert.That(control.GetVisualDescendants().OfType<TextBlock>()
                        .Any(text => text.Text == vm.Header && text.IsEffectivelyVisible), Is.True);
                    var tree = control.GetVisualDescendants().OfType<TreeLineDecorator>().Single();
                    Assert.That(tree.IsEffectivelyVisible, Is.True);
                    Assert.That(tree.LineBrush, Is.Not.Null);
                    var editors = control.GetVisualDescendants().OfType<NumberEditor<float>>().ToArray();
                    Assert.That(editors.Select(e => e.Header), Is.EqualTo(new[] { "X", "Y", "Z" }));
                    Assert.That(editors.All(e => e.MenuContent != null && e.Bounds.Width > 0), Is.True);
                    var componentButton = ((Control)editors[0].MenuContent!).GetVisualDescendants().OfType<Button>().First();
                    componentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    HeadlessTestHelpers.Render(3);
                    var componentMenu = (FAMenuFlyout)componentButton.ContextFlyout!;
                    AssertComponentMenu(componentMenu, separated: false, canSeparate: false);
                    componentMenu.Hide();
                    var input = editors[0].GetVisualDescendants().OfType<TextBox>().Single();
                    input.Focus();
                    input.SelectAll();
                    window.KeyTextInput("75");
                    editors[1].GetVisualDescendants().OfType<TextBox>().Single().Focus();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(owner.Value.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(new Vector3(75, 20, 30)));
                    history.History.Undo();
                    Assert.That(owner.Value.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(new Vector3(10, 20, 30)));
                    HeadlessTestHelpers.Render(3);
                    history.History.Undo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(combined.IsEffectivelyVisible, Is.True);
                    Assert.That(control.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
                    history.History.Redo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(combined.IsEffectivelyVisible, Is.False);
                    Assert.That(control.GetVisualDescendants().OfType<NumberEditor<float>>().Count(e => e.IsEffectivelyVisible), Is.EqualTo(3));
                    Assert.That(Beutl.Language.Strings.SeparateComponents, Is.EqualTo(language == "ja" ? "成分を分離" : "Separate components"));
                    if (Environment.GetEnvironmentVariable("BEUTL_COMPONENT_CAPTURE") is { } directory)
                    {
                        using var frame = window.CaptureRenderedFrame();
                        frame!.Save(System.IO.Path.Combine(directory, $"components-{width}-{language}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }

                    var parentMenu = control.GetVisualDescendants().OfType<PropertyEditorMenu>()
                        .Single(item => ReferenceEquals(item.DataContext, vm) && item.IsEffectivelyVisible);
                    var parentButton = parentMenu.GetVisualDescendants().OfType<Button>().First();
                    parentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var parentFlyout = (FAMenuFlyout)parentButton.ContextFlyout!;
                    HeadlessTestHelpers.Render(3);
                    AssertComponentMenu(parentFlyout, separated: true);
                    var unseparate = parentFlyout.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "unseparateComponentsItem");
                    Assert.That(unseparate.IsVisible, Is.True);
                    Assert.That(unseparate.Text, Is.EqualTo(language == "ja" ? "成分の分離を解除" : "Unseparate components"));
                    if (Environment.GetEnvironmentVariable("BEUTL_COMPONENT_CAPTURE") is { } menuDirectory)
                    {
                        using var frame = TopLevel.GetTopLevel(unseparate)!.CaptureRenderedFrame();
                        frame!.Save(System.IO.Path.Combine(menuDirectory, $"components-menu-{language}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                    unseparate.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
                    parentFlyout.Hide();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(combined.IsEffectivelyVisible, Is.True);
                    Assert.That(tree.IsEffectivelyVisible, Is.False);
                    Assert.That(control.GetVisualDescendants().OfType<NumberEditor<float>>(), Is.Empty);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    HeadlessTestHelpers.Render(3);
                    AssertComponentMenu(menu, separated: false, uniformVisible: true);
                    menu.Hide();
                    history.History.Undo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(combined.IsEffectivelyVisible, Is.False);
                    Assert.That(tree.IsEffectivelyVisible, Is.True);
                    Assert.That(control.GetVisualDescendants().OfType<NumberEditor<float>>().Count(e => e.IsEffectivelyVisible), Is.EqualTo(3));
                    parentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    HeadlessTestHelpers.Render(3);
                    AssertComponentMenu(parentFlyout, separated: true);
                    parentFlyout.Hide();
                    history.History.Redo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(combined.IsEffectivelyVisible, Is.True);
                    Assert.That(tree.IsEffectivelyVisible, Is.False);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    HeadlessTestHelpers.Render(3);
                    AssertComponentMenu(menu, separated: false, uniformVisible: true);
                    menu.Hide();

                    // A pre-existing aggregate expression must not divert the parent button to the expression editor.
                    owner.Value.Expression = Expression.Create<Vector3>("new System.Numerics.Vector3(7, 8, 9)");
                    vm.SeparateComponents();
                    var split = (IComponentAnimation<Vector3>)owner.Value.GetPropertyAnimation()!;
                    var x = (AnimatableProperty<float>)split.Components[0];
                    var xAnimation = new KeyFrameAnimation<float>();
                    xAnimation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 10 } });
                    xAnimation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 20 }, KeyTime = { Value = MediaTime.FromSeconds(2) } });
                    x.SetKeyFrameAnimation(xAnimation);
                    ((IExpressionProperty<float>)split.Components[1]).Expression = Expression.Create<float>("40f");
                    history.History.Commit();
                    HeadlessTestHelpers.Render(3);
                    parentButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    HeadlessTestHelpers.Render(3);
                    AssertComponentMenu(parentFlyout, separated: true, hasAggregateExpression: true);
                    Assert.That(owner.Value.HasExpression, Is.True);
                    Assert.That(window.OpenedPopups.Any(popup => popup.Child is ExpressionEditorFlyoutPresenter), Is.False);
                    var removeExpression = parentFlyout.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "removeExpressionItem");
                    Assert.That(removeExpression.IsEnabled, Is.True);
                    if (Environment.GetEnvironmentVariable("BEUTL_COMPONENT_CAPTURE") is { } expressionDirectory)
                    {
                        using var frame = TopLevel.GetTopLevel(removeExpression)!.CaptureRenderedFrame();
                        frame!.Save(System.IO.Path.Combine(expressionDirectory, $"components-existing-expression-{language}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                    removeExpression.RaiseEvent(new RoutedEventArgs(FAMenuFlyoutItem.ClickEvent));
                    HeadlessTestHelpers.Render(3);
                    Assert.That(owner.Value.HasExpression, Is.False);
                    Assert.That(owner.Value.GetPropertyAnimation(), Is.SameAs(split));
                    Assert.That(x.GetKeyFrameAnimation(), Is.SameAs(xAnimation));
                    Assert.That(owner.Value.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(new Vector3(10, 40, 30)));
                    Assert.That(removeExpression.IsVisible, Is.False);
                    history.History.Undo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(owner.Value.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(new Vector3(7, 8, 9)));
                    Assert.That(removeExpression.IsVisible, Is.True);
                    history.History.Redo();
                    HeadlessTestHelpers.Render(3);
                    Assert.That(owner.Value.GetPropertyAnimation(), Is.SameAs(split));
                    Assert.That(xAnimation.KeyFrames.Count, Is.EqualTo(2));
                    Assert.That(removeExpression.IsVisible, Is.False);
                    parentFlyout.Hide();
                }
                finally { window.Close(); }
            }
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    private static void AssertComponentMenu(FAMenuFlyout menu, bool separated, bool canSeparate = true, bool uniformVisible = false,
        bool hasAggregateExpression = false)
    {
        Assert.That(menu.IsOpen, Is.True);
        var items = menu.Items.OfType<Control>().ToDictionary(item => item.Name!);
        Assert.Multiple(() =>
        {
            Assert.That(items["separateComponentsItem"].IsVisible, Is.EqualTo(canSeparate && !separated), "separate components");
            Assert.That(items["unseparateComponentsItem"].IsVisible, Is.EqualTo(separated), "unseparate components");
            Assert.That(items["uniformEditorToggle"].IsVisible, Is.EqualTo(uniformVisible), "uniform value");
            foreach (string name in new[] { "editAnimationItem", "editInlineAnimationItem", "removeAnimationItem", "editExpressionItem" })
                Assert.That(items[name].IsVisible, Is.EqualTo(!separated), name);
            Assert.That(items["removeExpressionItem"].IsVisible, Is.EqualTo(!separated || hasAggregateExpression), "remove expression");
            Assert.That(items["expressionSeparator"].IsVisible, Is.EqualTo(!separated || hasAggregateExpression), "expression separator");
        });
    }
}

public sealed partial class ComponentTestObject<T> : EngineObject
{
    public ComponentTestObject() => ScanProperties<ComponentTestObject<T>>();
    public AnimatableProperty<T> Value { get; } = new(default!);
}

internal sealed class ComponentTestRoot : Hierarchical, IHierarchicalRoot
{
    public event EventHandler<IHierarchical>? DescendantAttached;
    public event EventHandler<IHierarchical>? DescendantDetached;
    public void OnDescendantAttached(IHierarchical descendant) => DescendantAttached?.Invoke(this, descendant);
    public void OnDescendantDetached(IHierarchical descendant) => DescendantDetached?.Invoke(this, descendant);
}
