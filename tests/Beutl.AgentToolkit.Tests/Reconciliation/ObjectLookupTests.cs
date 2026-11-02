using Beutl.AgentToolkit.Reconciliation;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Reconciliation;

// ObjectLookup stands in for FindById and ReferenceScope.Find during a plan, so it must answer as they do.
public sealed class ObjectLookupTests
{
    private sealed class PlainRoot : CoreObject;

    private static Element AddElement(Scene scene)
    {
        var element = new Element { Uri = new Uri(Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, $"{Guid.NewGuid():N}.belm")) };
        scene.Children.Add(element);
        return element;
    }

    [Test]
    public void Find_answers_as_FindById_does_for_every_object_of_the_tree()
    {
        Scene scene = TestWorkspace.CreateScene();
        var rect = new RectShape();
        var animation = new KeyFrameAnimation<float>();
        animation.KeyFrames.Add(new KeyFrame<float> { Value = { Value = 50 } }, out _);
        rect.Opacity.SetKeyFrameAnimation(animation);
        AddElement(scene).AddObject(rect);
        AddElement(scene);
        var lookup = new ObjectLookup(scene);

        ICoreObject[] objects = [.. ChildObjects.Walk(scene).OfType<ICoreObject>()];

        Assert.Multiple(() =>
        {
            Assert.That(objects, Does.Contain(scene).And.Contain(rect));
            Assert.That(objects.OfType<IKeyFrame>(), Is.Not.Empty);
            foreach (ICoreObject item in objects)
            {
                Assert.That(lookup.Find(item.Id.Value), Is.SameAs(scene.FindById(item.Id.Value)));
            }

            Assert.That(lookup.Find(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public void Find_answers_a_repeated_id_with_the_object_FindById_finds()
    {
        Scene scene = TestWorkspace.CreateScene();
        Element first = AddElement(scene);
        Element second = AddElement(scene);
        second.Id.Value = first.Id.Value;

        ICoreObject? found = new ObjectLookup(scene).Find(first.Id.Value);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.SameAs(first));
            Assert.That(found, Is.SameAs(scene.FindById(first.Id.Value)));
        });
    }

    // A copied file can hold the same Id twice in a project: a reference resolves within its scene first.
    [Test]
    public void FindReference_answers_as_ReferenceScope_does()
    {
        Scene other = TestWorkspace.CreateScene("other");
        Scene holder = TestWorkspace.CreateScene("holder");
        var project = new Project();
        project.Items.Add(other);
        project.Items.Add(holder);
        _ = new BeutlApplication { Project = { Value = project } };
        Element inHolder = AddElement(holder);
        Element inOther = AddElement(other);
        Element repeatedInOther = AddElement(other);
        Element repeatedInHolder = AddElement(holder);
        repeatedInHolder.Id.Value = repeatedInOther.Id.Value;
        var lookup = new ObjectLookup(holder);

        Assert.Multiple(() =>
        {
            Assert.That(lookup.FindReference(inHolder.Id.Value), Is.SameAs(inHolder));
            Assert.That(lookup.FindReference(inOther.Id.Value), Is.SameAs(inOther));
            Assert.That(lookup.FindReference(repeatedInOther.Id.Value), Is.SameAs(repeatedInHolder));
            Assert.That(lookup.FindReference(other.Id.Value), Is.SameAs(other));
            Assert.That(lookup.FindReference(Guid.NewGuid()), Is.Null);
            foreach (Guid id in (Guid[])[inHolder.Id.Value, inOther.Id.Value, repeatedInOther.Id.Value, other.Id.Value])
            {
                Assert.That(lookup.FindReference(id), Is.SameAs(ReferenceScope.Find(holder, id)));
            }
        });
    }

    [Test]
    public void FindReference_searches_a_root_outside_any_hierarchy_alone()
    {
        var root = new PlainRoot();
        var lookup = new ObjectLookup(root);

        Assert.Multiple(() =>
        {
            Assert.That(lookup.FindReference(root.Id.Value), Is.SameAs(root));
            Assert.That(lookup.FindReference(Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public void FindReference_searches_a_hierarchical_root_outside_any_project_alone()
    {
        var root = new RectShape();
        var lookup = new ObjectLookup(root);

        Assert.Multiple(() =>
        {
            Assert.That(lookup.FindReference(root.Id.Value), Is.SameAs(root));
            Assert.That(lookup.FindReference(Guid.NewGuid()), Is.Null);
        });
    }
}
