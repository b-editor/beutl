using Beutl.Engine.Expressions;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;

namespace Beutl.UnitTests.Core;

// A copied file can bring an Id that another scene already has: a reference resolves within its own scene first, then
// in the rest of the project (b-editor/beutl#2885).
[TestFixture]
public class ReferenceScopeTests
{
    private BeutlApplication _app = null!;
    private Scene _first = null!;
    private Scene _second = null!;

    [SetUp]
    public void SetUp()
    {
        _first = new Scene(64, 64, "First");
        _second = new Scene(64, 64, "Second");
        var project = new Project { Items = { _first, _second } };
        _app = new BeutlApplication { Projects = { project } };
    }

    [TearDown]
    public void TearDown()
    {
        _app.Projects.Clear();
    }

    [Test]
    public void AReferenceExpression_ResolvesToTheObjectInItsOwnScene()
    {
        RectShape inFirst = AddShape(_first);
        RectShape inSecond = AddShape(_second, inFirst.Id.Value);
        inFirst.Width.Value = 10;
        inSecond.Width.Value = 20;
        var presenter = new DrawablePresenter { Target = { Expression = new ReferenceExpression<Drawable?>(inFirst.Id.Value) } };
        var width = new RectShape { Width = { Expression = new ReferenceExpression<float>(inFirst.Id.Value, "Width") } };
        AddElement(_second, presenter, width);

        Assert.Multiple(() =>
        {
            Assert.That(presenter.Target.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.SameAs(inSecond));
            Assert.That(width.Width.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.EqualTo(20));
        });
    }

    [Test]
    public void AReferenceExpression_FindsAnObjectInAnotherSceneWhenItsOwnHasNone()
    {
        RectShape inFirst = AddShape(_first);
        var presenter = new DrawablePresenter { Target = { Expression = new ReferenceExpression<Drawable?>(inFirst.Id.Value) } };
        AddElement(_second, presenter);

        Assert.That(presenter.Target.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.SameAs(inFirst));
    }

    [Test]
    public async Task ADeferredReference_ResolvesToTheObjectInTheScene()
    {
        RectShape inFirst = AddShape(_first);
        RectShape inSecond = AddShape(_second, inFirst.Id.Value);
        RectShape holder = AddShape(_second);

        ICoreObject resolved = await new ReferenceResolver(holder, inFirst.Id.Value).Resolve();

        Assert.That(resolved, Is.SameAs(inSecond));
    }

    // Two loaded copies of one project share every Id: a reference in each copy resolves within that copy.
    [Test]
    public async Task AReference_ResolvesWithinItsOwnProject_WhenTwoCopiesAreLoaded()
    {
        (Project original, RectShape originalTarget, DrawablePresenter originalPresenter, RectShape originalWidth,
            RectShape originalHolder) = CreateCopy(null);
        (Project copy, RectShape copyTarget, DrawablePresenter copyPresenter, RectShape copyWidth, RectShape copyHolder)
            = CreateCopy(originalTarget.Id.Value, originalPresenter.Id.Value, originalWidth.Id.Value, originalHolder.Id.Value,
                ((Scene)original.Items[0]).Id.Value, ((Scene)original.Items[1]).Id.Value);
        originalTarget.Width.Value = 10;
        copyTarget.Width.Value = 20;
        var app = new BeutlApplication { Projects = { original, copy } };
        var context = new EvaluationContext(MediaTime.Zero);

        foreach (Project? active in (Project?[])[original, copy, null])
        {
            app.ActiveProject.Value = active;
            Assert.Multiple(() =>
            {
                Assert.That(originalPresenter.Target.Evaluate(context), Is.SameAs(originalTarget));
                Assert.That(copyPresenter.Target.Evaluate(context), Is.SameAs(copyTarget));
                Assert.That(originalWidth.Width.Evaluate(context), Is.EqualTo(10));
                Assert.That(copyWidth.Width.Evaluate(context), Is.EqualTo(20));
                Assert.That(ReferenceScope.Find(originalHolder, original.Id.Value), Is.SameAs(original));
                Assert.That(ReferenceScope.Find(copyHolder, copy.Id.Value), Is.SameAs(copy));
            });
        }

        Assert.That(await new ReferenceResolver(originalHolder, originalTarget.Id.Value).Resolve(), Is.SameAs(originalTarget));
        Assert.That(await new ReferenceResolver(copyHolder, copyTarget.Id.Value).Resolve(), Is.SameAs(copyTarget));
    }

    // A deferred reference waits for its object to join its own project, not another loaded copy.
    [Test]
    public async Task ADeferredReference_IgnoresAnObjectThatJoinsAnotherProject()
    {
        var holderScene = new Scene(64, 64, "Holder");
        var otherScene = new Scene(64, 64, "Other");
        var app = new BeutlApplication { Projects = { new Project { Items = { holderScene } }, new Project { Items = { otherScene } } } };
        RectShape holder = AddShape(holderScene);
        Guid id = Guid.NewGuid();

        Task<ICoreObject> resolving = new ReferenceResolver(holder, id).Resolve();
        AddShape(otherScene, id);
        Assert.That(resolving.IsCompleted, Is.False);
        RectShape own = AddShape(holderScene, id);

        Assert.That(await resolving.WaitAsync(TimeSpan.FromSeconds(5)), Is.SameAs(own));
        GC.KeepAlive(app);
    }

    // A project with a scene holding the target and a scene referring to it, with the given Ids or new ones.
    private static (Project Project, RectShape Target, DrawablePresenter Presenter, RectShape Width, RectShape Holder) CreateCopy(
        Guid? targetId, Guid presenterId = default, Guid widthId = default, Guid holderId = default,
        Guid firstSceneId = default, Guid secondSceneId = default)
    {
        var first = new Scene(64, 64, "First");
        var second = new Scene(64, 64, "Second");
        if (targetId is not null)
        {
            first.Id.Value = firstSceneId;
            second.Id.Value = secondSceneId;
        }

        var project = new Project { Items = { first, second } };
        RectShape target = AddShape(first, targetId);
        var presenter = new DrawablePresenter { Target = { Expression = new ReferenceExpression<Drawable?>(target.Id.Value) } };
        var width = new RectShape { Width = { Expression = new ReferenceExpression<float>(target.Id.Value, "Width") } };
        var holder = new RectShape();
        if (targetId is not null)
        {
            presenter.Id.Value = presenterId;
            width.Id.Value = widthId;
            holder.Id.Value = holderId;
        }

        AddElement(second, presenter, width, holder);
        return (project, target, presenter, width, holder);
    }

    private static RectShape AddShape(Scene scene, Guid? id = null)
    {
        var shape = new RectShape();
        if (id is { } given)
            shape.Id.Value = given;
        AddElement(scene, shape);
        return shape;
    }

    private static void AddElement(Scene scene, params Drawable[] drawables)
    {
        var element = new Element { Start = { Value = MediaTime.Zero }, Length = { Value = MediaTime.FromSeconds(1) }, ZIndex = { Value = scene.Children.Count } };
        foreach (Drawable drawable in drawables)
            element.AddObject(drawable);
        scene.Children.Add(element);
    }
}
