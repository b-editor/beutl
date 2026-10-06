using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ElementPropertyTab.ViewModels;
using Beutl.Editor.Components.ElementPropertyTab.Views;
using Beutl.Editor.Models;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.NodeGraph;
using Beutl.NodeGraph.Nodes;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class LockedClipPropertyTests
{
    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task LockedClip_BlocksPropertyKeyFrameAndEnableEdits(bool lockLayer, bool lockAfterSelection)
    {
        (EditViewModel editor, Element element, RectShape shape) = await OpenClip();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.Zero, Value = 100 });
        animation.KeyFrames.Add(new KeyFrame<float> { KeyTime = TimeSpan.FromSeconds(1), Value = 200 });
        shape.Width.Animation = animation;
        var layer = new TimelineLayer { ZIndex = element.ZIndex };
        editor.Scene.Layers.Add(layer);
        if (!lockAfterSelection) SetLock(true);
        Select(editor, element);
        using var tab = new ElementPropertyTabViewModel(editor);
        if (lockAfterSelection) SetLock(true);
        HeadlessTestHelpers.Settle();

        var property = WidthEditor(tab, shape);
        var number = new NumberEditor<float>();
        property.Accept(number);
        editor.HistoryManager.Commit("test setup");
        int before = editor.HistoryManager.UndoCount;
        Assert.Multiple(() =>
        {
            Assert.That(editor.Scene.IsElementLocked(element), Is.True);
            Assert.That(tab.CanEdit.Value, Is.False);
            Assert.That(property.CanEdit.Value, Is.False);
            Assert.That(number.IsReadOnly, Is.True);
        });

        number.Value = 250;
        number.RaiseEvent(new PropertyEditorValueChangedEventArgs<float>(250, 100, PropertyEditor.ValueChangedEvent, number));
        number.RaiseEvent(new PropertyEditorValueChangedEventArgs<float>(250, 100, PropertyEditor.ValueConfirmedEvent, number));
        property.SetValue(100, 250);
        property.SetValue(250);
        property.Reset();
        Assert.That(property.SetCurrentValueAndGetCoerced(250), Is.EqualTo(100));
        property.InsertKeyFrame(TimeSpan.FromSeconds(0.5));
        property.RemoveKeyFrame(TimeSpan.Zero);
        property.RemoveAnimation();
        property.PrepareToEditAnimation();
        Assert.That(property.SetExpression("1 + 2", out _), Is.False);

        ((IEditorClock)editor.GetService(typeof(IEditorClock))!).CurrentTime.Value = TimeSpan.FromSeconds(0.5);
        tab.Items[0].IsExpanded.Value = false;
        var menu = new PropertyEditorMenu { DataContext = property };
        var objectView = new EngineObjectPropertyView { DataContext = tab.Items[0] };
        var window = new Window
        {
            Width = 640,
            Height = 480,
            Content = new StackPanel { Children = { menu, objectView } }
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button diamond = menu.GetVisualDescendants().OfType<Button>().First();
            Click(window, diamond);
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(2));
            Assert.That(MenuItem(diamond, "editAnimationItem").IsEnabled, Is.False);
            Assert.That(MenuItem(diamond, "editInlineAnimationItem").IsEnabled, Is.False);
            Assert.That(MenuItem(diamond, "removeAnimationItem").IsEnabled, Is.False);
            Assert.That(MenuItem(diamond, "resetItem").IsEnabled, Is.False);
            diamond.ContextFlyout!.Hide();
            HeadlessTestHelpers.Settle();

            ToggleButton visibility = objectView.FindControl<ToggleButton>("VisibilityButton")!;
            Assert.That(visibility.IsEffectivelyEnabled, Is.False);
            Click(window, visibility);
            tab.Items[0].IsEnabled.Value = false; // Also reject a queued binding write.
            objectView.Remove_Click(null, new Avalonia.Interactivity.RoutedEventArgs());
            Assert.Multiple(() =>
            {
                Assert.That(shape.IsEnabled, Is.True);
                Assert.That(tab.Items[0].IsEnabled.Value, Is.True);
                Assert.That(element.Objects, Does.Contain(shape));
                Assert.That(shape.Width.CurrentValue, Is.EqualTo(100));
                Assert.That(shape.Width.Animation, Is.SameAs(animation));
                Assert.That(animation.KeyFrames.Select(x => x.Value), Is.EqualTo(new[] { 100f, 200f }));
                Assert.That(shape.Width.Expression, Is.Null);
                Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
            });

            // Keyframe navigation remains available while the clip is read-only.
            property.KeyFrameIndex.Value = 1;
            Assert.That(((IEditorClock)editor.GetService(typeof(IEditorClock))!).CurrentTime.Value,
                Is.EqualTo(TimeSpan.FromSeconds(1)));
            SetLock(false);
            HeadlessTestHelpers.Settle();
            Assert.That(property.CanEdit.Value, Is.True);
            Assert.That(number.IsReadOnly, Is.False);
            Assert.That(visibility.IsEffectivelyEnabled, Is.True);
            property.SetValue(200, 300);
            property.InsertKeyFrame(TimeSpan.FromSeconds(1.5));
            Click(window, visibility);
            Assert.That(animation.KeyFrames[1].Value, Is.EqualTo(300));
            Assert.That(animation.KeyFrames, Has.Count.EqualTo(3));
            Assert.That(shape.IsEnabled, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.GreaterThan(before));
        }
        finally { window.Close(); }

        void SetLock(bool value)
        {
            if (lockLayer) layer.IsLocked = value;
            else element.IsLocked = value;
        }
    }

    [AvaloniaTest]
    public async Task PropertyTab_TracksLayerChangesUndoRedoAndSelection()
    {
        (EditViewModel editor, Element element, RectShape shape) = await OpenClip();
        Select(editor, element);
        using var tab = new ElementPropertyTabViewModel(editor);
        var property = WidthEditor(tab, shape);
        var layer = new TimelineLayer { ZIndex = element.ZIndex, IsLocked = true };
        editor.Scene.Layers.Add(layer);
        Assert.That(property.CanEdit.Value, Is.False);
        layer.ZIndex = 1;
        Assert.That(property.CanEdit.Value, Is.True);
        element.ZIndex = 1;
        Assert.That(property.CanEdit.Value, Is.False);
        editor.Scene.Layers.Remove(layer);
        Assert.That(property.CanEdit.Value, Is.True);

        editor.HistoryManager.Commit("test setup");
        var layerService = (ILayerAttributeService)editor.GetService(typeof(ILayerAttributeService))!;
        layerService.SetLocked(editor.Scene, 1, true);
        Assert.That(property.CanEdit.Value, Is.False);
        Assert.That(editor.HistoryManager.Undo(), Is.True);
        HeadlessTestHelpers.Settle();
        Assert.That(property.CanEdit.Value, Is.True);
        Assert.That(editor.HistoryManager.Redo(), Is.True);
        HeadlessTestHelpers.Settle();
        Assert.That(property.CanEdit.Value, Is.False);

        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 2,
            new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Element second = editor.Scene.Children.Last();
        Select(editor, second);
        Assert.That(property.IsDisposed, Is.True);
        Assert.That(tab.CanEdit.Value, Is.True);
        element.IsLocked = true;
        Assert.That(tab.CanEdit.Value, Is.True);
        Select(editor, null);
        Assert.That(tab.CanEdit.Value, Is.False);
        Assert.That(tab.Items, Is.Empty);
        element.Objects.Add(new RectShape());
        Assert.That(tab.Items, Is.Empty, "Clearing selection must unsubscribe the old clip's objects.");
        Select(editor, second);
        Assert.That(tab.CanEdit.Value, Is.True);
    }

    [AvaloniaTest]
    public async Task ExpressionMenu_AllowsUnlockedEditsAndRejectsEditsAfterLocking()
    {
        (EditViewModel editor, Element element, RectShape shape) = await OpenClip();
        Select(editor, element);
        using var tab = new ElementPropertyTabViewModel(editor);
        var property = WidthEditor(tab, shape);
        Assert.That(property.SetExpression("1 + 2", out string? error), Is.True, error);
        var expression = shape.Width.Expression;
        var menu = new PropertyEditorMenu { DataContext = property };
        var window = new Window { Content = menu, Width = 320, Height = 240 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            Button button = menu.GetVisualDescendants().OfType<Button>().First();
            button.ContextFlyout!.ShowAt(button);
            HeadlessTestHelpers.Render();
            Assert.That(property.CanEdit.Value, Is.False);
            Assert.That(property.CanEditProperty.Value, Is.True);
            Assert.That(MenuItem(button, "editExpressionItem").IsEnabled, Is.True);
            Assert.That(MenuItem(button, "removeExpressionItem").IsEnabled, Is.True);

            element.IsLocked = true;
            HeadlessTestHelpers.Settle();
            Assert.That(MenuItem(button, "editExpressionItem").IsEnabled, Is.False);
            Assert.That(MenuItem(button, "removeExpressionItem").IsEnabled, Is.False);
            int before = editor.HistoryManager.UndoCount;
            Assert.That(property.SetExpression("4 + 5", out _), Is.False);
            property.RemoveExpression();
            property.PrepareToEditAnimation();
            Assert.That(shape.Width.Expression, Is.SameAs(expression));
            Assert.That(shape.Width.Animation, Is.Null);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));

            element.IsLocked = false;
            Assert.That(property.SetExpression("4 + 5", out error), Is.True, error);
            property.RemoveExpression();
            Assert.That(shape.Width.Expression, Is.Null);
            Assert.That(property.CanEdit.Value, Is.True);
        }
        finally { window.Close(); }
    }

    [AvaloniaTest]
    public async Task NestedEditors_RejectValueEnableAndCollectionEditsWhileLocked()
    {
        (EditViewModel editor, Element element, RectShape shape) = await OpenClip();
        var translation = new TranslateTransform();
        translation.X.CurrentValue = 10;
        var group = new TransformGroup();
        group.Children.Add(translation);
        shape.Transform.CurrentValue = group;
        var brush = new SolidColorBrush(Colors.Red);
        shape.Fill.CurrentValue = brush;
        Select(editor, element);
        using var tab = new ElementPropertyTabViewModel(editor);
        var transform = Editors(tab.Items[0].Properties).OfType<TransformEditorViewModel>().Single();
        transform.IsExpanded.Value = true;
        var child = (TransformEditorViewModel)transform.Group.Value!.Items[0].Context!;
        child.IsExpanded.Value = true;
        var x = child.Properties.Value!.Properties.OfType<NumberEditorViewModel<float>>()
            .Single(p => ReferenceEquals(p.PropertyAdapter.GetEngineProperty(), translation.X));
        var fill = Editors(tab.Items[0].Properties).OfType<BrushEditorViewModel>()
            .Single(p => ReferenceEquals(p.PropertyAdapter.GetEngineProperty(), shape.Fill));
        editor.HistoryManager.Commit("test setup");
        element.IsLocked = true;
        int before = editor.HistoryManager.UndoCount;

        x.SetValue(10, 20);
        child.IsEnabled.Value = false;
        transform.AddItem(new TranslateTransform());
        transform.Group.Value.AddItem(new TranslateTransform());
        transform.Group.Value.RemoveItem(0);
        transform.Group.Value.Initialize();
        fill.SetColor(Colors.Red, Colors.Blue);
        fill.SetValue(brush, new SolidColorBrush(Colors.Blue));
        Assert.Multiple(() =>
        {
            Assert.That(x.CanEdit.Value, Is.False);
            Assert.That(translation.X.CurrentValue, Is.EqualTo(10));
            Assert.That(translation.IsEnabled, Is.True);
            Assert.That(child.IsEnabled.Value, Is.True);
            Assert.That(group.Children, Is.EqualTo(new[] { translation }));
            Assert.That(shape.Fill.CurrentValue, Is.SameAs(brush));
            Assert.That(brush.Color.CurrentValue, Is.EqualTo(Colors.Red));
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));
        });

        element.IsLocked = false;
        x.SetValue(10, 20);
        child.IsEnabled.Value = false;
        transform.AddItem(new TranslateTransform());
        fill.SetColor(Colors.Red, Colors.Blue);
        Assert.That(translation.X.CurrentValue, Is.EqualTo(20));
        Assert.That(translation.IsEnabled, Is.False);
        Assert.That(group.Children, Has.Count.EqualTo(2));
        Assert.That(brush.Color.CurrentValue, Is.EqualTo(Colors.Blue));
    }

    [AvaloniaTest]
    public void NonClipEditor_RemainsEditable()
    {
        var shape = new RectShape();
        var adapter = new AnimatablePropertyAdapter<float>((AnimatableProperty<float>)shape.Width, shape);
        using var property = new NumberEditorViewModel<float>(adapter);
        Assert.That(property.CanEdit.Value, Is.True);
        Assert.That(property.SetCurrentValueAndGetCoerced(123), Is.EqualTo(123));
        Assert.That(shape.Width.CurrentValue, Is.EqualTo(123));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task EmbeddedGraphRename_RejectsConfirmationAfterLocking(bool lockLayer)
    {
        (EditViewModel editor, Element element, _) = await OpenClip();
        var drawable = new NodeGraphDrawable();
        var node = new LayerInputNode { Name = "original" };
        drawable.Model.CurrentValue!.Nodes.Add(node);
        ((IElementObjectService)editor.GetService(typeof(IElementObjectService))!).Add(element, drawable);
        Select(editor, element);
        using var tab = new ElementPropertyTabViewModel(editor);
        var graph = Editors(tab.Items.Single(item => item.Model == drawable).Properties)
            .OfType<GraphModelEditorViewModel>().Single();
        GraphModelNodeMemberViewModel member = graph.NodeMembers.Single();
        member.UpdateName("unlocked rename");
        Assert.That(node.Name, Is.EqualTo("unlocked rename"));

        // The flyout confirmation keeps this callback even if the clip is locked later.
        Action<string?> confirmRename = member.UpdateName;
        var layer = new TimelineLayer { ZIndex = element.ZIndex };
        editor.Scene.Layers.Add(layer);
        if (lockLayer) layer.IsLocked = true;
        else element.IsLocked = true;
        int before = editor.HistoryManager.UndoCount;
        confirmRename("locked rename");
        Assert.That(node.Name, Is.EqualTo("unlocked rename"));
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));

        layer.IsLocked = false;
        element.IsLocked = false;
        confirmRename("rename after unlock");
        Assert.That(node.Name, Is.EqualTo("rename after unlock"));
        Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public async Task NestedAudioEffectToggle_DisablesWhileLocked(bool lockLayer)
    {
        (EditViewModel editor, Element element, _) = await OpenClip();
        var effect = new DelayEffect();
        var group = new AudioEffectGroup();
        group.Children.Add(effect);
        var sound = new SourceSound();
        sound.Effect.CurrentValue = group;
        ((IElementObjectService)editor.GetService(typeof(IElementObjectService))!).Add(element, sound);
        Select(editor, element);
        using var tab = new ElementPropertyTabViewModel(editor);
        var parent = Editors(tab.Items.Single(item => item.Model == sound).Properties)
            .OfType<AudioEffectEditorViewModel>().Single();
        parent.IsExpanded.Value = true;
        var child = (AudioEffectEditorViewModel)parent.Group.Value!.Items[0].Context!;
        var view = new AudioEffectListItemEditor { DataContext = child };
        AssertEffectToggleLock(editor, element, effect, view, lockLayer);
    }

    [AvaloniaTest]
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task NestedFilterEffectToggle_DisablesBothPresenterBranches(bool presenter, bool lockLayer)
    {
        (EditViewModel editor, Element element, RectShape shape) = await OpenClip();
        FilterEffect effect = presenter ? new FilterEffectPresenter() : new Blur();
        var group = new FilterEffectGroup();
        group.Children.Add(effect);
        shape.FilterEffect.CurrentValue = group;
        Select(editor, element);
        using var tab = new ElementPropertyTabViewModel(editor);
        var parent = Editors(tab.Items[0].Properties).OfType<FilterEffectEditorViewModel>().Single();
        parent.IsExpanded.Value = true;
        var child = (FilterEffectEditorViewModel)parent.Group.Value!.Items[0].Context!;
        Assert.That(child.IsPresenter.Value, Is.EqualTo(presenter));
        var view = new FilterEffectListItemEditor { DataContext = child };
        AssertEffectToggleLock(editor, element, effect, view, lockLayer);
    }

    private static void AssertEffectToggleLock(EditViewModel editor, Element element, EngineObject effect, Control view, bool lockLayer)
    {
        var layer = new TimelineLayer { ZIndex = element.ZIndex };
        editor.Scene.Layers.Add(layer);
        editor.HistoryManager.Commit("test setup");
        var window = new Window { Content = view, Width = 640, Height = 480 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ToggleButton toggle = view.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Classes.Contains("size-24x24") && button.IsEffectivelyVisible);
            Assert.That(toggle.IsEffectivelyEnabled, Is.True);
            if (lockLayer) layer.IsLocked = true;
            else element.IsLocked = true;
            HeadlessTestHelpers.Settle();
            int before = editor.HistoryManager.UndoCount;
            Assert.That(toggle.IsEffectivelyEnabled, Is.False);
            Click(window, toggle);
            Assert.That(effect.IsEnabled, Is.True);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before));

            layer.IsLocked = false;
            element.IsLocked = false;
            HeadlessTestHelpers.Settle();
            Assert.That(toggle.IsEffectivelyEnabled, Is.True);
            Click(window, toggle);
            Assert.That(effect.IsEnabled, Is.False);
            Assert.That(editor.HistoryManager.UndoCount, Is.EqualTo(before + 1));
        }
        finally { window.Close(); }
    }

    private static async Task<(EditViewModel Editor, Element Element, RectShape Shape)> OpenClip()
    {
        await TestReset.ResetShellAsync();
        string root = Path.Combine(BeutlHomeIsolation.CurrentHome!, $"clip-lock-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Project project = (await TestShell.Project.CreateProject(640, 480, 30, 44100, "clip-lock", root))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        var editor = (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value;
        var adder = (IElementAdder)editor.GetService(typeof(IElementAdder))!;
        await adder.AddAsync([new ElementDescription(TimeSpan.Zero, TimeSpan.FromSeconds(2), 0,
            new ElementSource.EngineObject(() => new RectShape()))], CancellationToken.None);
        Element element = scene.Children.First();
        var shape = (RectShape)element.Objects[0];
        shape.Width.CurrentValue = 100;
        return (editor, element, shape);
    }

    private static void Select(EditViewModel editor, Element? element)
    {
        ((IEditorSelection)editor.GetService(typeof(IEditorSelection))!).SelectedObject.Value = element;
        HeadlessTestHelpers.Settle();
    }

    private static NumberEditorViewModel<float> WidthEditor(ElementPropertyTabViewModel tab, RectShape shape)
        => Editors(tab.Items[0].Properties).OfType<NumberEditorViewModel<float>>()
            .Single(x => ReferenceEquals(x.PropertyAdapter.GetEngineProperty(), shape.Width));

    private static IEnumerable<IPropertyEditorContext> Editors(IReadOnlyList<IPropertyEditorContext?> properties)
    {
        foreach (IPropertyEditorContext? property in properties)
        {
            if (property is PropertyEditorGroupContext group)
            {
                foreach (IPropertyEditorContext child in Editors(group.Properties))
                    yield return child;
            }
            else if (property != null)
            {
                yield return property;
            }
        }
    }

    private static FAMenuFlyoutItem MenuItem(Button button, string name)
        => ((FAMenuFlyout)button.ContextFlyout!).Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == name);

    private static void Click(Window window, Control control)
    {
        Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(center, MouseButton.Left);
        window.MouseUp(center, MouseButton.Left);
        HeadlessTestHelpers.Settle();
    }
}
