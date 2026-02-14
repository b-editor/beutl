using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;

namespace Beutl.UnitTests.Engine;

[TestFixture]
public class PresenterTests
{
    [Test]
    public void BrushPresenter_ShouldImplementIPresenter()
    {
        // Arrange & Act
        var presenter = new BrushPresenter();

        // Assert
        Assert.That(presenter, Is.InstanceOf<IPresenter<Brush>>());
        Assert.That(presenter, Is.InstanceOf<IPresenter>());
    }

    [Test]
    public void BrushPresenter_TargetType_ShouldBeBrush()
    {
        // Arrange
        IPresenter presenter = new BrushPresenter();

        // Act & Assert
        Assert.That(presenter.TargetType, Is.EqualTo(typeof(Brush)));
    }

    [Test]
    public void BrushPresenter_Target_ShouldBeNullByDefault()
    {
        // Arrange
        var presenter = new BrushPresenter();

        // Act
        var target = presenter.Target;

        // Assert
        Assert.That(target, Is.Not.Null);
        Assert.That(target.GetValue(null!), Is.Null);
    }

    [Test]
    public void FilterEffectPresenter_ShouldImplementIPresenter()
    {
        // Arrange & Act
        var presenter = new FilterEffectPresenter();

        // Assert
        Assert.That(presenter, Is.InstanceOf<IPresenter<FilterEffect>>());
        Assert.That(presenter, Is.InstanceOf<IPresenter>());
    }

    [Test]
    public void FilterEffectPresenter_TargetType_ShouldBeFilterEffect()
    {
        // Arrange
        IPresenter presenter = new FilterEffectPresenter();

        // Act & Assert
        Assert.That(presenter.TargetType, Is.EqualTo(typeof(FilterEffect)));
    }

    [Test]
    public void FilterEffectPresenter_Target_ShouldBeNullByDefault()
    {
        // Arrange
        var presenter = new FilterEffectPresenter();

        // Act
        var target = presenter.Target;

        // Assert
        Assert.That(target, Is.Not.Null);
        Assert.That(target.GetValue(null!), Is.Null);
    }

    [Test]
    public void TransformPresenter_ShouldImplementIPresenter()
    {
        // Arrange & Act
        var presenter = new TransformPresenter();

        // Assert
        Assert.That(presenter, Is.InstanceOf<IPresenter<Transform>>());
        Assert.That(presenter, Is.InstanceOf<IPresenter>());
    }

    [Test]
    public void TransformPresenter_TargetType_ShouldBeTransform()
    {
        // Arrange
        IPresenter presenter = new TransformPresenter();

        // Act & Assert
        Assert.That(presenter.TargetType, Is.EqualTo(typeof(Transform)));
    }

    [Test]
    public void TransformPresenter_Target_ShouldBeNullByDefault()
    {
        // Arrange
        var presenter = new TransformPresenter();

        // Act
        var target = presenter.Target;

        // Assert
        Assert.That(target, Is.Not.Null);
        Assert.That(target.GetValue(null!), Is.Null);
    }

    [Test]
    public void DrawablePresenter_ShouldImplementIPresenter()
    {
        // Arrange & Act
        var presenter = new DrawablePresenter();

        // Assert
        Assert.That(presenter, Is.InstanceOf<IPresenter<Drawable>>());
        Assert.That(presenter, Is.InstanceOf<IPresenter>());
    }

    [Test]
    public void DrawablePresenter_TargetType_ShouldBeDrawable()
    {
        // Arrange
        IPresenter presenter = new DrawablePresenter();

        // Act & Assert
        Assert.That(presenter.TargetType, Is.EqualTo(typeof(Drawable)));
    }

    [Test]
    public void DrawablePresenter_Target_ShouldBeNullByDefault()
    {
        // Arrange
        var presenter = new DrawablePresenter();

        // Act
        var target = presenter.Target;

        // Assert
        Assert.That(target, Is.Not.Null);
        Assert.That(target.GetValue(null!), Is.Null);
    }
}
