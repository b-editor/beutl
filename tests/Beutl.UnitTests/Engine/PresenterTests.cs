using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class PresenterTests
{
    public static IEnumerable<TestCaseData> Presenters()
    {
        yield return Case(static () => new BrushPresenter(), typeof(Brush));
        yield return Case(static () => new FilterEffectPresenter(), typeof(FilterEffect));
        yield return Case(static () => new TransformPresenter(), typeof(Transform));
        yield return Case(static () => new DrawablePresenter(), typeof(Drawable));

        static TestCaseData Case(Func<IPresenter> create, Type targetType)
            => new TestCaseData(create, targetType).SetArgDisplayNames(targetType.Name);
    }

    [TestCaseSource(nameof(Presenters))]
    public void TargetType_ShouldBeThePresentedType(Func<IPresenter> create, Type targetType)
    {
        Assert.That(create().TargetType, Is.EqualTo(targetType));
    }

    [TestCaseSource(nameof(Presenters))]
    public void Target_ShouldBeNullByDefault(Func<IPresenter> create, Type targetType)
    {
        IPresenter presenter = create();
        // IPresenter has no Target, so it is read through the typed interface the presenter implements.
        var target = (IProperty?)typeof(IPresenter<>).MakeGenericType(targetType)
            .GetProperty(nameof(IPresenter<CoreObject>.Target))!
            .GetValue(presenter);

        Assert.That(target, Is.Not.Null);
        Assert.That(target!.CurrentValue, Is.Null);
    }
}
