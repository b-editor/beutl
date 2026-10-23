using System.Text.Json.Nodes;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Engine.Expressions;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.Serialization;
using Beutl.UnitTests.TestInfrastructure;

namespace Beutl.UnitTests.Engine.Expressions;

// Keyframes are the bottom of a property's expression chain and give the values the animation gave.
[TestFixture]
public class KeyFrameExpressionTests
{
    private static AnimatableProperty<float> Make()
    {
        var property = new AnimatableProperty<float>(0);
        property.SetAttributes("Value", []);
        return property;
    }

    [TestCase(false, new[] { 4, 5.25, 5.5, 7 }, new[] { 0f, 25, 50, 100 })]
    [TestCase(true, new[] { 0.25, 0.5, 5.5 }, new[] { 25f, 50, 100 })]
    public void KeyFrames_GiveTheAnimatedValueAtEachTime(bool useGlobalClock, double[] seconds, float[] expected)
    {
        var owner = new AnimationSamplerTestRoot { TimeRange = { Value = new TimeRange(MediaTime.FromSeconds(5), MediaTime.FromSeconds(2)) } };
        var property = Make();
        property.SetOwnerObject(owner);
        var keyFrames = TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1), useGlobalClock);
        property.SetKeyFrameAnimation(keyFrames);

        float[] values = seconds.Select(s => property.Evaluate(new CompositionContext(MediaTime.FromSeconds(s)))).ToArray();
        float[] evaluated = seconds.Select(s => ((IExpression<float>)keyFrames).Evaluate(
            TestHelper.CreateExpressionContext(MediaTime.FromSeconds(s)))).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(values, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(evaluated, Is.EqualTo(expected).Within(1e-4f));
            Assert.That(property.Expression, Is.SameAs(keyFrames));
            Assert.That(property.HasExpression, Is.False);
        }
    }

    [Test]
    public void UserExpression_WinsOverKeyFramesAndGivesThemBackWhenRemoved()
    {
        var property = Make();
        var keyFrames = TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1));
        property.SetKeyFrameAnimation(keyFrames);
        var context = new CompositionContext(MediaTime.FromSeconds(0.5));

        property.Expression = Expression.Create<float>("42");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.Evaluate(context), Is.EqualTo(42f));
            Assert.That(property.HasExpression, Is.True);
            Assert.That(property.Expression!.ExpressionString, Is.EqualTo("42"));
            Assert.That(property.Expression.Replaced, Is.SameAs(keyFrames));
            Assert.That(property.GetKeyFrameAnimation(), Is.SameAs(keyFrames));
        }

        property.Expression = null;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.Expression, Is.SameAs(keyFrames));
            Assert.That(property.HasExpression, Is.False);
            Assert.That(property.Evaluate(context), Is.EqualTo(50f).Within(1e-4f));
        }
    }

    [Test]
    public void KeyFrames_AddedUnderAnExpression_GoBelowIt()
    {
        var property = Make();
        property.Expression = Expression.Create<float>("42");
        var keyFrames = TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1));

        property.SetKeyFrameAnimation(keyFrames);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.Evaluate(new CompositionContext(MediaTime.FromSeconds(0.5))), Is.EqualTo(42f));
            Assert.That(property.Expression!.ExpressionString, Is.EqualTo("42"));
            Assert.That(property.Expression.Replaced, Is.SameAs(keyFrames));
        }

        property.SetKeyFrameAnimation(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.Expression!.ExpressionString, Is.EqualTo("42"));
            Assert.That(property.Expression.Replaced, Is.Null);
            Assert.That(property.GetKeyFrameAnimation(), Is.Null);
        }
    }

    [Test]
    public void FailingExpression_FallsBackToTheKeyFrames()
    {
        var property = Make();
        property.SetKeyFrameAnimation(TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1)));
        property.Expression = Expression.Create<float>("1 +");

        float value = property.Evaluate(new CompositionContext(MediaTime.FromSeconds(0.5)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(50f).Within(1e-4f));
            Assert.That(property.ExpressionError, Is.Not.Null);
        }
    }

    [Test]
    public void AnExpressionThatKeepsNothing_CannotHoldKeyFrames()
    {
        var property = Make();
        var expression = new ConstantExpression(3);
        property.Expression = expression;

        Assert.Throws<InvalidOperationException>(() => property.SetKeyFrameAnimation(TestAnimations.Linear(0f, 1f, MediaTime.FromSeconds(1))));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(property.Expression, Is.SameAs(expression));
            Assert.That(property.GetKeyFrameAnimation(), Is.Null);
        }
    }

    [Test]
    public void KeyFrames_AreEvaluatedWithoutAnExpressionContext()
    {
        var property = Make();
        var keyFrames = TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1));
        property.SetKeyFrameAnimation(keyFrames);
        var context = new CompositionContext(MediaTime.FromSeconds(0.5));
        property.Evaluate(context);
        keyFrames.GetAnimatedValue(context.Time);

        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            keyFrames.GetAnimatedValue(context.Time);
        long interpolation = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
            property.Evaluate(context);
        long evaluation = GC.GetAllocatedBytesForCurrentThread() - start;

        // Unoptimized code boxes values inside the interpolation; the property must add nothing to it.
        Assert.That(evaluation, Is.LessThanOrEqualTo(interpolation));
    }

    [Test]
    public void ObjectWithExpressionAndKeyFrames_SavesTheSameJsonAfterLoading()
    {
        var shape = new RectShape();
        shape.Width.SetKeyFrameAnimation(TestAnimations.Linear(0f, 100f, MediaTime.FromSeconds(1)));
        shape.Width.Expression = Expression.Create<float>("42");
        JsonObject saved = CoreSerializer.SerializeToJsonObject(shape);

        var loaded = (RectShape)CoreSerializer.DeserializeFromJsonObject(saved, typeof(RectShape));
        JsonObject resaved = CoreSerializer.SerializeToJsonObject(loaded);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resaved.ToJsonString(), Is.EqualTo(saved.ToJsonString()));
            Assert.That(saved["Animations"]?["Width"]?["KeyFrames"]?.AsArray(), Has.Count.EqualTo(2));
            Assert.That(saved["Expressions"]?.AsObject().Select(p => p.Key), Is.EqualTo(new[] { "Width" }));
            Assert.That(loaded.Width.Expression!.ExpressionString, Is.EqualTo("42"));
            Assert.That(loaded.Width.Expression.Replaced, Is.SameAs(loaded.Width.GetKeyFrameAnimation()).And.Not.Null);
        }
    }

    private sealed class ConstantExpression(float value) : IExpression<float>
    {
        public string ExpressionString => "constant";

        public bool Validate(out string? error)
        {
            error = null;
            return true;
        }

        public float Evaluate(ExpressionContext context) => value;
    }
}
