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
        _app = new BeutlApplication { Project = project };
    }

    [TearDown]
    public void TearDown()
    {
        _app.Project = null;
    }

    [Test]
    public void AReferenceExpression_ResolvesToTheObjectInItsOwnScene()
    {
        RectShape inFirst = AddShape(_first);
        RectShape inSecond = AddShape(_second, inFirst.Id);
        inFirst.Width.Value = 10;
        inSecond.Width.Value = 20;
        var presenter = new DrawablePresenter { Target = { Expression = new ReferenceExpression<Drawable?>(inFirst.Id) } };
        var width = new RectShape { Width = { Expression = new ReferenceExpression<float>(inFirst.Id, "Width") } };
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
        var presenter = new DrawablePresenter { Target = { Expression = new ReferenceExpression<Drawable?>(inFirst.Id) } };
        AddElement(_second, presenter);

        Assert.That(presenter.Target.Evaluate(new EvaluationContext(MediaTime.Zero)), Is.SameAs(inFirst));
    }

    [Test]
    public async Task ADeferredReference_ResolvesToTheObjectInTheScene()
    {
        RectShape inFirst = AddShape(_first);
        RectShape inSecond = AddShape(_second, inFirst.Id);
        RectShape holder = AddShape(_second);

        ICoreObject resolved = await new ReferenceResolver(holder, inFirst.Id).Resolve();

        Assert.That(resolved, Is.SameAs(inSecond));
    }

    private static RectShape AddShape(Scene scene, Guid? id = null)
    {
        var shape = new RectShape();
        if (id is { } given)
            shape.Id = given;
        AddElement(scene, shape);
        return shape;
    }

    private static void AddElement(Scene scene, params Drawable[] drawables)
    {
        var element = new Element { Start = MediaTime.Zero, Length = MediaTime.FromSeconds(1), ZIndex = scene.Children.Count };
        foreach (Drawable drawable in drawables)
            element.AddObject(drawable);
        scene.Children.Add(element);
    }
}
