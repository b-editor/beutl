using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.PropertyEditors;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ReferencePropertyEditorTests
{
    private static readonly Type[] s_owners = [typeof(DrawablePresenter), typeof(DrawableTimeController),
        typeof(FilterEffectPresenter), typeof(TransformPresenter), typeof(BrushPresenter), typeof(SceneDrawable), typeof(SceneSound)];

    private static readonly Type[] s_nonSceneOwners = s_owners.Where(type => type != typeof(SceneDrawable) && type != typeof(SceneSound)).ToArray();

    public static IEnumerable<TestCaseData> PropertiesAndSurfaces()
        => s_owners.SelectMany(type => Enum.GetValues<EditorSurface>().Select(surface => new TestCaseData(type, surface)));

    [AvaloniaTest]
    [TestCaseSource(nameof(PropertiesAndSurfaces))]
    public void EveryDeclaredReferenceHasAnEditableTargetPicker(Type ownerType, EditorSurface surface)
    {
        using var fixture = new Fixture(ownerType, surface);
        fixture.Show();
        fixture.Reference.SetTarget(fixture.Target);
        HeadlessTestHelpers.Render();

        ReferenceEditor picker = fixture.Window.GetVisualDescendants().OfType<ReferenceEditor>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Control, Is.TypeOf<ReferencePropertyEditor>().Or.TypeOf<ListItemEditorHost>());
            Assert.That(picker.IsEffectivelyEnabled, Is.True);
            Assert.That(picker.TargetName, Is.EqualTo(fixture.Target.Name.Value));
            Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object), Does.Contain(fixture.Target).And.Not.Contain(fixture.Owner));
            Assert.That(fixture.Reference.GetAvailableTargets().All(t => fixture.Property.ValueType.IsInstanceOfType(t.Object)), Is.True);
        }
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_owners))]
    public void SelectingAndClearingTargetsCanBeUndoneAndRedone(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);

        fixture.Reference.SetTarget(fixture.Target);
        Assert.That(((IReferenceExpression)fixture.Property.Expression!).ObjectId, Is.EqualTo(fixture.Target.Id.Value));
        Assert.That(fixture.Property.Value, Is.Null);
        Assert.That(fixture.History.History.UndoCount, Is.EqualTo(1));
        fixture.Reference.SetTarget(null);
        Assert.That(fixture.Property.Expression, Is.Null);
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(MessageStrings.PropertyUnset));

        Assert.That(fixture.History.History.Undo(), Is.True);
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(fixture.Target.Name.Value));
        Assert.That(fixture.History.History.Undo(), Is.True);
        Assert.That(fixture.Property.Expression, Is.Null);
        Assert.That(fixture.History.History.Redo(), Is.True);
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(fixture.Target.Name.Value));
    }

    [AvaloniaTest]
    [TestCase(typeof(DrawablePresenter))]
    [TestCase(typeof(SceneDrawable))]
    public void RetargetingAndRenamingUpdateTheLabelWithoutMovingThePlayhead(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        fixture.Reference.SetTarget(fixture.Target);
        CoreObject replacement;
        if (fixture.Target is Scene)
        {
            var scene = new Scene { Name = { Value = "Second scene" } };
            fixture.Project.Items.Add(scene);
            replacement = scene;
        }
        else
        {
            var shape = new RectShape { Name = { Value = "Second rectangle" } };
            fixture.Element.Objects.Add(shape);
            replacement = shape;
        }

        fixture.Reference.SetTarget(replacement);
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(replacement.Name.Value));
        replacement.Name.Value = "Renamed target";
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo("Renamed target"));
        fixture.RemoveTarget();
        fixture.Target.Name.Value = "Removed old target";
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo("Renamed target"));
        fixture.Reference.SetTarget(new EngineObject());
        Assert.That(((IReferenceExpression)fixture.Property.Expression!).ObjectId, Is.EqualTo(replacement.Id.Value));
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_owners))]
    public void RemovingTheTargetUpdatesTheLabelWithoutMovingThePlayhead(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        fixture.Show();
        fixture.Reference.SetTarget(fixture.Target);
        HeadlessTestHelpers.Render();
        ReferenceEditor picker = fixture.Window.GetVisualDescendants().OfType<ReferenceEditor>().Single();
        Assert.That(picker.TargetName, Is.EqualTo(fixture.Target.Name.Value));

        fixture.RemoveTarget();
        HeadlessTestHelpers.Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(MessageStrings.PropertyUnset));
            Assert.That(picker.TargetName, Is.EqualTo(MessageStrings.PropertyUnset));
            Assert.That(((IReferenceExpression)fixture.Property.Expression!).ObjectId, Is.EqualTo(fixture.Target.Id.Value));
        }
    }

    [AvaloniaTest]
    public void UndoingTargetRemovalRestoresTheLabelWithoutMovingThePlayhead()
    {
        using var fixture = new Fixture(typeof(DrawablePresenter));
        fixture.Reference.SetTarget(fixture.Target);
        fixture.RemoveTarget();
        fixture.History.History.Commit("Remove target");
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(MessageStrings.PropertyUnset));
        fixture.Target.Name.Value = "Renamed while removed";
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(MessageStrings.PropertyUnset));

        Assert.That(fixture.History.History.Undo(), Is.True);
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(fixture.Target.Name.Value));
        Assert.That(fixture.History.History.Redo(), Is.True);
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(MessageStrings.PropertyUnset));
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_owners))]
    public void UndoingTargetRemovalAfterMovingThePlayheadRestoresTheLabel(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        fixture.Show();
        fixture.Reference.SetTarget(fixture.Target);
        HeadlessTestHelpers.Render();
        ReferenceEditor picker = fixture.Window.GetVisualDescendants().OfType<ReferenceEditor>().Single();
        Assert.That(picker.TargetName, Is.EqualTo(fixture.Target.Name.Value));

        fixture.RemoveTarget();
        fixture.History.History.Commit("Remove target");
        fixture.Clock.Seek(MediaTime.FromSeconds(1));
        HeadlessTestHelpers.Render();
        Assert.That(picker.TargetName, Is.EqualTo(MessageStrings.PropertyUnset));

        Assert.That(fixture.History.History.Undo(), Is.True);
        HeadlessTestHelpers.Render();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((IExpression<CoreObject?>)fixture.Property.Expression!).Evaluate(
                new ExpressionContext(fixture.Clock.CurrentTime.Value, fixture.Property, new PropertyLookup(fixture.Project))),
                Is.SameAs(fixture.Target));
            Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(fixture.Target.Name.Value));
            Assert.That(picker.TargetName, Is.EqualTo(fixture.Target.Name.Value));
            Assert.That(fixture.Clock.CurrentTime.Value, Is.EqualTo(MediaTime.FromSeconds(1)));
        }
    }

    [AvaloniaTest]
    public void RemovingTheTargetsElementUpdatesTheLabelWithoutMovingThePlayhead()
    {
        using var fixture = new Fixture(typeof(BrushPresenter));
        var scene = (Scene)fixture.Element.HierarchicalParent!;
        var targetElement = new Element();
        var shape = fixture.Element.Objects.OfType<RectShape>().Single();
        fixture.Element.Objects.Remove(shape);
        targetElement.Objects.Add(shape);
        scene.Children.Add(targetElement);
        fixture.Reference.SetTarget(fixture.Target);
        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(fixture.Target.Name.Value));

        scene.Children.Remove(targetElement);

        Assert.That(fixture.Reference.CurrentTargetName.Value, Is.EqualTo(MessageStrings.PropertyUnset));
    }

    [AvaloniaTest]
    public void PickingATargetClearsTheStoredValueAndUndoRestoresIt()
    {
        using var fixture = new Fixture(typeof(DrawablePresenter));
        fixture.Property.Value = fixture.Target;
        fixture.History.History.Commit("Stored target");

        fixture.Reference.SetTarget(fixture.Target);
        Assert.That(fixture.Property.Value, Is.Null);
        Assert.That(fixture.Property.Expression, Is.InstanceOf<IReferenceExpression>());
        fixture.History.History.Undo();
        Assert.That(fixture.Property.Value, Is.SameAs(fixture.Target));
        Assert.That(fixture.Property.Expression, Is.Null);
    }

    [AvaloniaTest]
    [TestCase(typeof(SceneDrawable))]
    [TestCase(typeof(SceneSound))]
    public void TheContainingSceneCannotBePickedOrAssigned(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        var scene = (Scene)fixture.Element.HierarchicalParent!;
        fixture.Property.Value = fixture.Target;
        fixture.History.History.Commit("Stored target");

        fixture.Reference.SetTarget(scene);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object), Does.Not.Contain(scene));
            Assert.That(fixture.Property.Value, Is.SameAs(fixture.Target));
            Assert.That(fixture.Property.Expression, Is.Null);
            Assert.That(fixture.History.History.UndoCount, Is.EqualTo(1));
        }
    }

    [AvaloniaTest]
    [TestCase(typeof(SceneDrawable), false, false)]
    [TestCase(typeof(SceneSound), false, false)]
    [TestCase(typeof(SceneDrawable), true, false)]
    [TestCase(typeof(SceneSound), true, false)]
    [TestCase(typeof(SceneDrawable), false, true)]
    [TestCase(typeof(SceneSound), false, true)]
    [TestCase(typeof(SceneDrawable), true, true)]
    [TestCase(typeof(SceneSound), true, true)]
    public void ScenesReachingTheContainingSceneCannotBePickedOrAssigned(Type ownerType, bool indirect, bool storedValue)
    {
        using var fixture = new Fixture(ownerType);
        var scene = (Scene)fixture.Element.HierarchicalParent!;
        var blocked = new Scene { Name = { Value = "Would close a cycle" } };
        fixture.Project.Items.Add(blocked);
        Scene next = scene;
        if (indirect)
        {
            next = new Scene();
            fixture.Project.Items.Add(next);
            var sound = new SceneSound();
            sound.ReferencedScene.Expression = new ReferenceExpression<Scene?>(scene.Id.Value);
            next.Children.Add(new Element { Objects = { new Beutl.Audio.SoundGroup { Children = { sound } } } });
        }

        AddSceneReference(blocked, next, ownerType, storedValue);
        fixture.Reference.SetTarget(fixture.Target);
        IExpression? original = fixture.Property.Expression;
        int undoCount = fixture.History.History.UndoCount;

        fixture.Reference.SetTarget(blocked);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object),
                Does.Contain(fixture.Target).And.Not.Contain(blocked).And.Not.Contain(scene));
            Assert.That(fixture.Property.Expression, Is.SameAs(original));
            Assert.That(fixture.History.History.UndoCount, Is.EqualTo(undoCount));
        }
    }

    [AvaloniaTest]
    [TestCase(typeof(SceneDrawable))]
    [TestCase(typeof(SceneSound))]
    public void SharedSceneReferencesRemainSelectableAndUnrelatedCyclesTerminate(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        var target = (Scene)fixture.Target;
        var shared = new Scene();
        var other = new Scene();
        fixture.Project.Items.AddRange([shared, other]);
        AddSceneReference(target, shared, typeof(SceneDrawable));
        AddSceneReference(target, other, typeof(SceneSound));
        AddSceneReference(other, shared, typeof(SceneDrawable));

        fixture.Reference.SetTarget(target);
        Assert.That(((IReferenceExpression)fixture.Property.Expression!).ObjectId, Is.EqualTo(target.Id.Value));
        Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object), Does.Contain(target));

        AddSceneReference(shared, other, typeof(SceneSound));
        Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object), Does.Contain(target));
        fixture.Reference.SetTarget(null);
        Assert.That(fixture.Property.Expression, Is.Null);
    }

    [AvaloniaTest]
    [TestCase(typeof(SceneDrawable))]
    [TestCase(typeof(SceneSound))]
    public async Task ASceneBecomingRecursiveWhileThePickerIsOpenCannotBeAssigned(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        fixture.Show();
        ReferenceEditor editor = fixture.Window.GetVisualDescendants().OfType<ReferenceEditor>().Single();
        editor.RaiseEvent(new RoutedEventArgs(ReferenceEditor.SelectTargetRequestedEvent));
        HeadlessTestHelpers.Render();
        LibraryItemPickerFlyoutPresenter picker = fixture.Window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
        picker.SelectedItem = picker.ReferenceItems!.Single(item => ((TargetObjectInfo)item.UserData).Object == fixture.Target);
        AddSceneReference((Scene)fixture.Target, (Scene)fixture.Element.HierarchicalParent!, ownerType);
        fixture.History.History.Commit("Add scene reference");
        int undoCount = fixture.History.History.UndoCount;

        HeadlessTestHelpers.Click(picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AcceptButton"));
        await Task.Yield();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Property.Expression, Is.Null);
            Assert.That(fixture.History.History.UndoCount, Is.EqualTo(undoCount));
        }
    }

    private static void AddSceneReference(Scene source, Scene target, Type ownerType, bool storedValue = false)
    {
        var owner = (EngineObject)Activator.CreateInstance(ownerType)!;
        var property = (IExpressionProperty<Scene?>)owner.Properties.Single(p => p is IExpressionProperty { IsReference: true });
        if (storedValue)
            property.Value = target;
        else
            property.Expression = new ReferenceExpression<Scene?>(target.Id.Value);
        source.Children.Add(new Element { Objects = { owner } });
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_nonSceneOwners))]
    public void NonSceneContainingObjectsCannotBePickedOrAssigned(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        fixture.Element.Objects.Remove(fixture.Owner);
        EngineObject container = CreateReferenceContainer(fixture.Owner);
        fixture.Element.Objects.Add(container);
        fixture.Property.Value = fixture.Target;
        fixture.History.History.Commit("Stored target");

        fixture.Reference.SetTarget(container);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object),
                Does.Contain(fixture.Target).And.Not.Contain(container).And.Not.Contain(fixture.Owner));
            Assert.That(fixture.Property.Value, Is.SameAs(fixture.Target));
            Assert.That(fixture.Property.Expression, Is.Null);
            Assert.That(fixture.History.History.UndoCount, Is.EqualTo(1));
        }

        using var resource = container.ToResource(new Beutl.Composition.CompositionContext(MediaTime.Zero));
    }

    public static IEnumerable<TestCaseData> NonSceneReferenceGraphs()
        => s_nonSceneOwners.SelectMany(type => new[] { false, true }.SelectMany(indirect =>
            new[] { false, true }.Select(stored => new TestCaseData(type, indirect, stored))));

    [AvaloniaTest]
    [TestCaseSource(nameof(NonSceneReferenceGraphs))]
    public void NonSceneReferenceCyclesCannotBePickedOrAssigned(Type ownerType, bool indirect, bool storedValue)
    {
        using var fixture = new Fixture(ownerType);
        var presenter = (EngineObject)Activator.CreateInstance(ownerType)!;
        EngineObject blocked = CreateReferenceContainer(presenter);
        fixture.Element.Objects.Add(blocked);
        EngineObject next = fixture.Owner;
        if (indirect)
        {
            var middle = (EngineObject)Activator.CreateInstance(ownerType)!;
            next = CreateReferenceContainer(middle);
            fixture.Element.Objects.Add(next);
            SetReference(middle, fixture.Owner, storedValue);
        }

        SetReference(presenter, next, storedValue);
        fixture.Reference.SetTarget(fixture.Target);
        IExpression? original = fixture.Property.Expression;
        int undoCount = fixture.History.History.UndoCount;

        fixture.Reference.SetTarget(blocked);
        fixture.Reference.SetTarget(fixture.Owner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object),
                Does.Contain(fixture.Target).And.Not.Contain(blocked));
            Assert.That(fixture.Property.Expression, Is.SameAs(original));
            Assert.That(fixture.History.History.UndoCount, Is.EqualTo(undoCount));
        }
    }

    [AvaloniaTest]
    public void NonSceneCyclesAcrossReferenceTypesCannotBePickedOrAssigned()
    {
        using var fixture = new Fixture(typeof(BrushPresenter));
        fixture.Element.Objects.Remove(fixture.Owner);
        var shape = new RectShape { Fill = { Value = (Brush)fixture.Owner } };
        fixture.Element.Objects.Add(shape);
        var presenter = new DrawablePresenter();
        var blocked = new DrawableBrush(presenter);
        fixture.Element.Objects.Add(blocked);
        presenter.Target.Expression = new ReferenceExpression<Drawable?>(shape.Id.Value);
        fixture.History.History.Commit("Add drawable reference");
        int undoCount = fixture.History.History.UndoCount;

        fixture.Reference.SetTarget(blocked);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object),
                Does.Contain(fixture.Target).And.Not.Contain(blocked));
            Assert.That(fixture.Property.Expression, Is.Null);
            Assert.That(fixture.History.History.UndoCount, Is.EqualTo(undoCount));
        }
    }

    [AvaloniaTest]
    public void NonSceneSharedTargetsRemainSelectableAndUnrelatedCyclesTerminate()
    {
        using var fixture = new Fixture(typeof(FilterEffectPresenter));
        var first = new FilterEffectPresenter();
        var second = new FilterEffectPresenter();
        var shared = new FilterEffectGroup { Children = { new Blur() } };
        var target = new FilterEffectGroup { Children = { first, second } };
        fixture.Element.Objects.AddRange([shared, target]);
        SetReference(first, shared);
        SetReference(second, shared);

        fixture.Reference.SetTarget(target);
        Assert.That(((IReferenceExpression)fixture.Property.Expression!).ObjectId, Is.EqualTo(target.Id.Value));
        Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object), Does.Contain(target));

        var back = new FilterEffectPresenter();
        shared.Children.Add(back);
        SetReference(back, target);
        Assert.That(fixture.Reference.GetAvailableTargets().Select(t => t.Object), Does.Contain(target));
        fixture.Reference.SetTarget(null);
        Assert.That(fixture.Property.Expression, Is.Null);
    }

    [AvaloniaTest]
    [TestCaseSource(nameof(s_nonSceneOwners))]
    public async Task NonSceneReferenceCyclesAreRecheckedWhenThePickerConfirms(Type ownerType)
    {
        using var fixture = new Fixture(ownerType);
        var presenter = (EngineObject)Activator.CreateInstance(ownerType)!;
        EngineObject target = CreateReferenceContainer(presenter);
        fixture.Element.Objects.Add(target);
        fixture.Show();
        ReferenceEditor editor = fixture.Window.GetVisualDescendants().OfType<ReferenceEditor>().Single();
        editor.RaiseEvent(new RoutedEventArgs(ReferenceEditor.SelectTargetRequestedEvent));
        HeadlessTestHelpers.Render();
        LibraryItemPickerFlyoutPresenter picker = fixture.Window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
        picker.SelectedItem = picker.ReferenceItems!.Single(item => ((TargetObjectInfo)item.UserData).Object == target);
        SetReference(presenter, fixture.Owner);
        fixture.History.History.Commit("Add reference");
        int undoCount = fixture.History.History.UndoCount;

        HeadlessTestHelpers.Click(picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AcceptButton"));
        await Task.Yield();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fixture.Property.Expression, Is.Null);
            Assert.That(fixture.History.History.UndoCount, Is.EqualTo(undoCount));
        }
    }

    private static EngineObject CreateReferenceContainer(EngineObject child)
        => child switch
        {
            Drawable drawable => new DrawableGroup { Children = { drawable } },
            FilterEffect effect => new FilterEffectGroup { Children = { effect } },
            Transform transform => new TransformGroup { Children = { transform } },
            Brush brush => new DrawableBrush(new RectShape { Fill = { Value = brush } }),
            _ => throw new ArgumentOutOfRangeException(nameof(child))
        };

    private static void SetReference(EngineObject owner, CoreObject target, bool storedValue = false)
    {
        var property = owner.Properties.OfType<IExpressionProperty>().Single(p => p.IsReference);
        if (storedValue)
            property.Value = target;
        else
            property.Expression = (IExpression)Activator.CreateInstance(
                typeof(ReferenceExpression<>).MakeGenericType(property.ValueType), target.Id.Value)!;
    }

    [AvaloniaTest]
    public async Task ThePickerCancelsConfirmsAndIgnoresDuplicateRequests()
    {
        using var fixture = new Fixture(typeof(DrawablePresenter));
        fixture.Show();
        ReferenceEditor editor = fixture.Window.GetVisualDescendants().OfType<ReferenceEditor>().Single();
        HeadlessTestHelpers.Click(editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "PART_TargetPickerButton"));
        editor.RaiseEvent(new RoutedEventArgs(ReferenceEditor.SelectTargetRequestedEvent));
        HeadlessTestHelpers.Render();
        LibraryItemPickerFlyoutPresenter picker = fixture.Window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
        picker.SelectedItem = picker.ReferenceItems!.Single(item => ((TargetObjectInfo)item.UserData).Object == fixture.Target);
        HeadlessTestHelpers.Click(picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "DismissButton"));
        await Task.Yield();
        Assert.That(fixture.Property.Expression, Is.Null);

        editor.RaiseEvent(new RoutedEventArgs(ReferenceEditor.SelectTargetRequestedEvent));
        HeadlessTestHelpers.Render();
        picker = fixture.Window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
        picker.SelectedItem = picker.ReferenceItems!.Single(item => ((TargetObjectInfo)item.UserData).Object == fixture.Target);
        HeadlessTestHelpers.Click(picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AcceptButton"));
        await Task.Yield();

        Assert.That(((IReferenceExpression)fixture.Property.Expression!).ObjectId, Is.EqualTo(fixture.Target.Id.Value));
        Assert.That(fixture.History.History.UndoCount, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task LockingTheElementWhileThePickerIsOpenPreventsTheAssignment()
    {
        using var fixture = new Fixture(typeof(DrawableTimeController));
        fixture.Show();
        ReferenceEditor editor = fixture.Window.GetVisualDescendants().OfType<ReferenceEditor>().Single();
        editor.RaiseEvent(new RoutedEventArgs(ReferenceEditor.SelectTargetRequestedEvent));
        HeadlessTestHelpers.Render();
        LibraryItemPickerFlyoutPresenter picker = fixture.Window.GetVisualDescendants().OfType<LibraryItemPickerFlyoutPresenter>().Single();
        picker.SelectedItem = picker.ReferenceItems!.Single(item => ((TargetObjectInfo)item.UserData).Object == fixture.Target);
        fixture.Element.IsLocked.Value = true;
        HeadlessTestHelpers.Click(picker.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "AcceptButton"));
        await Task.Yield();

        Assert.That(fixture.Property.Expression, Is.Null);
        Assert.That(fixture.Reference.CanEditProperty.Value, Is.False);
        fixture.Reference.SetTarget(fixture.Target);
        Assert.That(fixture.Property.Expression, Is.Null);
    }

    [AvaloniaTest]
    public void ReferenceMenusHideTextExpressionsAndOrdinaryPropertiesKeepThem()
    {
        using var fixture = new Fixture(typeof(DrawablePresenter));
        fixture.Reference.SetTarget(fixture.Target);
        var menu = new PropertyEditorMenu { DataContext = fixture.Vm };
        Assert.That(menu.FindControl<FAMenuFlyoutItem>("editExpressionItem")!.IsVisible, Is.False);
        Assert.That(menu.FindControl<FAMenuFlyoutItem>("removeExpressionItem")!.IsVisible, Is.False);

        var shape = new RectShape();
        var (vm, _) = CreateControl(PropertyAdapterFactory.CreateAdapter(shape.Width, shape));
        using (vm)
        {
            menu.DataContext = vm;
            Assert.That(vm, Is.Not.InstanceOf<IReferencePropertyEditorViewModel>());
            Assert.That(menu.FindControl<FAMenuFlyoutItem>("editExpressionItem")!.IsVisible, Is.True);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly BeutlApplication _app;

        public Fixture(Type ownerType, EditorSurface surface = EditorSurface.Property)
        {
            var scene = new Scene();
            Project = new Project { Items = { scene } };
            _app = new BeutlApplication { Project = { Value = Project } };
            Element = new Element();
            scene.Children.Add(Element);
            Owner = (EngineObject)Activator.CreateInstance(ownerType)!;
            Element.Objects.Add(Owner);
            Property = Owner.Properties.OfType<IExpressionProperty>().Single(p => p.IsReference);
            var shape = new RectShape { Name = { Value = "Rectangle" } };
            Element.Objects.Add(shape);
            Target = Property.ValueType switch
            {
                var t when t == typeof(Brush) => shape.Fill.Value = new SolidColorBrush(Colors.Red) { Name = { Value = "Red" } },
                var t when t == typeof(FilterEffect) => shape.FilterEffect.Value = new Blur { Name = { Value = "Blur" } },
                var t when t == typeof(Transform) => shape.Transform.Value = new TranslateTransform { Name = { Value = "Move" } },
                var t when t == typeof(Scene) => new Scene { Name = { Value = "Shared scene" } },
                _ => shape,
            };
            if (Target is Scene referenced) Project.Items.Add(referenced);
            History = new HistoryScope(Project);
            Clock = new EditorClockImpl(scene);
            (Vm, Control) = CreateControl(PropertyAdapterFactory.CreateAdapter(Property, Owner), surface);
            Vm.Accept(new ServiceVisitor((typeof(HistoryManager), History.History), (typeof(Scene), scene),
                (typeof(Element), Element),
                (typeof(IEditorClock), Clock),
                (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
                (typeof(ExtensionProvider), TestShell.Extensions)));
            Reference = (IReferencePropertyEditorViewModel)Vm;
            Window = new Window { Content = Control, Width = 420, Height = 480 };
        }

        public Project Project { get; }
        public Element Element { get; }
        public EngineObject Owner { get; }
        public CoreObject Target { get; }
        public IExpressionProperty Property { get; }
        public HistoryScope History { get; }
        public EditorClockImpl Clock { get; }
        public PropertyEditorViewModel Vm { get; }
        public IReferencePropertyEditorViewModel Reference { get; }
        public Control Control { get; }
        public Window Window { get; }

        public void Show()
        {
            Window.Show();
            HeadlessTestHelpers.Render(3);
        }

        public void RemoveTarget()
        {
            switch (Target)
            {
                case Scene scene:
                    Project.Items.Remove(scene);
                    break;
                case Brush brush:
                    ((RectShape)brush.HierarchicalParent!).Fill.Value = null;
                    break;
                case FilterEffect effect:
                    ((RectShape)effect.HierarchicalParent!).FilterEffect.Value = null;
                    break;
                case Transform transform:
                    ((RectShape)transform.HierarchicalParent!).Transform.Value = null;
                    break;
                default:
                    Element.Objects.Remove((EngineObject)Target);
                    break;
            }
        }

        public void Dispose()
        {
            Window.Close();
            Vm.Dispose();
            Clock.Dispose();
            History.Dispose();
            _app.Project.Value = null;
        }
    }
}
