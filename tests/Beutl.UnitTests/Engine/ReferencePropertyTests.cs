using Beutl.Composition;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Editor.Operations;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Beutl.UnitTests.Editor.Services;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class ReferencePropertyTests
{
    [TestCase(typeof(DrawablePresenter), "Target", typeof(RectShape))]
    [TestCase(typeof(DrawableTimeController), "Target", typeof(RectShape))]
    [TestCase(typeof(FilterEffectPresenter), "Target", typeof(Blur))]
    [TestCase(typeof(TransformPresenter), "Target", typeof(TranslateTransform))]
    [TestCase(typeof(BrushPresenter), "Target", typeof(SolidColorBrush))]
    [TestCase(typeof(SceneDrawable), "ReferencedScene", typeof(Scene))]
    [TestCase(typeof(SceneSound), "ReferencedScene", typeof(Scene))]
    public void Value_DoesNotOwnOrWalkTheTarget(Type ownerType, string name, Type targetType)
    {
        var owner = (EngineObject)Activator.CreateInstance(ownerType)!;
        var target = (CoreObject)Activator.CreateInstance(targetType)!;
        IProperty property = owner.Properties.Single(p => p.Name == name);
        property.Value = target;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((IHierarchical)owner).HierarchicalChildren, Does.Not.Contain(target));
            Assert.That(((IHierarchical)target).HierarchicalParent, Is.Null);
            Assert.That(owner.FindById(target.Id.Value), Is.Null);
            Assert.That(new ObjectSearcher(owner, obj => ReferenceEquals(obj, target)).Search(), Is.Null);
            Assert.That(property.Value, Is.SameAs(target));
        }
    }

    [Test]
    public void SetOwnerObject_DoesNotAttachOrDetachAnExistingReferenceValue()
    {
        var target = new RectShape();
        var owner = new EngineObject();
        var replacement = new EngineObject();
        var property = new SimpleProperty<Drawable?>(target) { IsReference = true };

        property.SetOwnerObject(owner);
        property.SetOwnerObject(replacement);
        property.SetOwnerObject(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(((IHierarchical)owner).HierarchicalChildren, Is.Empty);
            Assert.That(((IHierarchical)replacement).HierarchicalChildren, Is.Empty);
            Assert.That(target.HierarchicalParent, Is.Null);
        }
    }

    [Test]
    public void ReplacingOrClearingAReference_LeavesItsOwnersAndEditNotificationsAlone()
    {
        var element = new Element();
        var first = new RectShape();
        var second = new RectShape();
        element.Objects.Add(first);
        element.Objects.Add(second);
        var presenter = new DrawablePresenter();
        int edits = 0;
        presenter.Edited += (_, _) => edits++;

        presenter.Target.Value = first;
        first.Width.Value = 42;
        presenter.Target.Value = second;
        second.Width.Value = 64;
        presenter.Target.Value = null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(edits, Is.EqualTo(3));
            Assert.That(first.HierarchicalParent, Is.SameAs(element));
            Assert.That(second.HierarchicalParent, Is.SameAs(element));
            Assert.That(((IHierarchical)presenter).HierarchicalChildren, Does.Not.Contain(first).And.Not.Contain(second));
        }
    }

    [Test]
    public void History_RecordsReferenceAssignmentsButNotEditsInsideTheTarget()
    {
        var presenter = new DrawablePresenter();
        var first = new RectShape();
        var second = new RectShape();
        presenter.Target.Value = first;
        var operations = new List<ChangeOperation>();
        using var observer = new EnginePropertyOperationObserver<Drawable?>(
            System.Reactive.Observer.Create<ChangeOperation>(operations.Add), presenter, presenter.Target,
            new OperationSequenceGenerator(), nameof(DrawablePresenter.Target));

        first.Width.Value = 42;
        presenter.Target.Value = second;
        second.Width.Value = 64;
        presenter.Target.Value = null;

        Assert.That(operations.OfType<IPropertyPathProvider>().Select(operation => operation.PropertyPath),
            Is.EqualTo(new[] { "Target", "Target" }));
    }

    [TestCase(typeof(SceneDrawable))]
    [TestCase(typeof(SceneSound))]
    public void Regenerate_KeepsIdsAndDoesNotRenewInlineReferenceValues(Type ownerType)
    {
        var target = new Scene();
        var inner = new Element();
        var probe = new CopyRenewalProbe();
        inner.Objects.Add(probe);
        target.Children.Add(inner);
        var owner = (EngineObject)Activator.CreateInstance(ownerType)!;
        owner.Properties.Single(p => p.Name == "ReferencedScene").Value = target;

        ObjectRegenerator.Regenerate(owner, ownerType, out ICoreSerializable copied);
        var copy = (EngineObject)copied;
        var reference = (Scene)copy.Properties.Single(p => p.Name == "ReferencedScene").Value!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(copy.Id.Value, Is.Not.EqualTo(owner.Id.Value));
            Assert.That(reference.Id.Value, Is.EqualTo(target.Id.Value));
            Assert.That(reference.Children.Single().Id.Value, Is.EqualTo(inner.Id.Value));
            Assert.That(reference.Children.Single().Objects.Single().Id.Value, Is.EqualTo(probe.Id.Value));
            Assert.That(((CopyRenewalProbe)reference.Children.Single().Objects.Single()).Renewals, Is.Zero);
            Assert.That(reference.HierarchicalParent, Is.Null);
            Assert.That(copy.FindById(target.Id.Value), Is.Null);
        }
    }

    [Test]
    public void Regenerate_ReferenceExpressionStillResolvesToTheSharedScene()
    {
        var project = new Project();
        var scene = new Scene();
        var target = new Scene();
        project.Items.Add(scene);
        project.Items.Add(target);
        _ = new BeutlApplication { Project = { Value = project } };
        var element = new Element();
        var drawable = new SceneDrawable();
        drawable.ReferencedScene.Expression = new ReferenceExpression<Scene?>(target.Id.Value);
        element.Objects.Add(drawable);
        scene.Children.Add(element);

        ObjectRegenerator.Regenerate(element, out Element copy);
        scene.Children.Add(copy);
        var copied = (SceneDrawable)copy.Objects.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(copied.ReferencedScene.Value, Is.Null);
            Assert.That(copied.ReferencedScene.Evaluate(CompositionContext.Default), Is.SameAs(target));
            Assert.That(target.HierarchicalParent, Is.SameAs(project));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Regenerate_RenewsATargetThatIsAlsoOwnedInsideTheCopy(bool expression)
    {
        var element = new Element();
        var target = new RectShape();
        var presenter = new DrawablePresenter();
        element.Objects.Add(presenter);
        element.Objects.Add(target);
        if (expression)
            presenter.Target.Expression = new ReferenceExpression<Drawable?>(target.Id.Value);
        else
            presenter.Target.Value = target;

        ObjectRegenerator.Regenerate(element, out Element copy);
        var copiedPresenter = (DrawablePresenter)copy.Objects[0];
        Guid copiedTargetId = copy.Objects[1].Id.Value;

        Assert.That(copiedTargetId, Is.Not.EqualTo(target.Id.Value));
        Assert.That(expression
            ? ((IReferenceExpression)copiedPresenter.Target.Expression!).ObjectId
            : copiedPresenter.Target.Value!.Id.Value, Is.EqualTo(copiedTargetId));
    }
}
