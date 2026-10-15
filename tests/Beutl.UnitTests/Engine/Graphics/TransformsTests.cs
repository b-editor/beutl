using Beutl.Composition;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.UnitTests.Engine.Graphics;

public class TransformsTests
{
    private static CompositionContext Context => CompositionContext.Default;

    [Test]
    public void TranslateTransform_DefaultIsIdentity()
    {
        var t = new TranslateTransform();
        Matrix m = t.CreateMatrix(Context);
        Assert.That(m, Is.EqualTo(Matrix.Identity));
    }

    [Test]
    public void TranslateTransform_FromXY_BuildsTranslateMatrix()
    {
        var t = new TranslateTransform(10f, 20f);
        Matrix m = t.CreateMatrix(Context);
        Assert.That(m, Is.EqualTo(Matrix.CreateTranslation(10f, 20f)));
    }

    [Test]
    public void TranslateTransform_FromPoint_StoresValues()
    {
        var t = new TranslateTransform(new Point(7f, -2f));
        Assert.Multiple(() =>
        {
            Assert.That(t.X.CurrentValue, Is.EqualTo(7f));
            Assert.That(t.Y.CurrentValue, Is.EqualTo(-2f));
        });
    }

    [Test]
    public void RotationTransform_DegreesProducesRotationMatrix()
    {
        var t = new RotationTransform(90f);
        Matrix expected = Matrix.CreateRotation(MathUtilities.Deg2Rad(90f));
        Matrix actual = t.CreateMatrix(Context);

        Assert.That(actual.M11, Is.EqualTo(expected.M11).Within(1e-5));
        Assert.That(actual.M12, Is.EqualTo(expected.M12).Within(1e-5));
        Assert.That(actual.M21, Is.EqualTo(expected.M21).Within(1e-5));
        Assert.That(actual.M22, Is.EqualTo(expected.M22).Within(1e-5));
    }

    [Test]
    public void ScaleTransform_FromXY_AppliesPercentScale()
    {
        var t = new ScaleTransform { ScaleX = { CurrentValue = 200f }, ScaleY = { CurrentValue = 50f } };
        Matrix m = t.CreateMatrix(Context);
        Assert.Multiple(() =>
        {
            Assert.That(m.M11, Is.EqualTo(2f).Within(1e-5));
            Assert.That(m.M22, Is.EqualTo(0.5f).Within(1e-5));
        });
    }

    [Test]
    public void ScaleTransform_OverallScaleIsApplied()
    {
        var t = new ScaleTransform
        {
            ScaleX = { CurrentValue = 100f },
            ScaleY = { CurrentValue = 100f },
            Scale = { CurrentValue = 50f },
        };
        Matrix m = t.CreateMatrix(Context);
        Assert.Multiple(() =>
        {
            Assert.That(m.M11, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(m.M22, Is.EqualTo(0.5f).Within(1e-5));
        });
    }

    [Test]
    public void SkewTransform_FromDegrees_ProducesMatrix()
    {
        var t = new SkewTransform { SkewX = { CurrentValue = 45f } };
        Matrix expected = Matrix.CreateSkew(MathUtilities.Deg2Rad(45f), 0f);
        Matrix actual = t.CreateMatrix(Context);
        Assert.That(actual.M21, Is.EqualTo(expected.M21).Within(1e-5));
    }

    [Test]
    public void MatrixTransform_DefaultIsIdentity()
    {
        var t = new MatrixTransform();
        Assert.That(t.CreateMatrix(Context), Is.EqualTo(Matrix.Identity));
    }

    [Test]
    public void MatrixTransform_StoresProvidedMatrix()
    {
        var matrix = new Matrix(2, 0, 0, 3, 5, 7);
        var t = new MatrixTransform { Matrix = { CurrentValue = matrix } };
        Assert.That(t.CreateMatrix(Context), Is.EqualTo(matrix));
    }

    [Test]
    public void TransformGroup_NoChildren_ReturnsIdentity()
    {
        var group = new TransformGroup();
        Assert.That(group.CreateMatrix(Context), Is.EqualTo(Matrix.Identity));
    }

    [Test]
    public void TransformGroup_CombinesChildrenInOrder()
    {
        var group = new TransformGroup();
        group.Children.Add(new TranslateTransform(10f, 0f));
        group.Children.Add(new ScaleTransform { ScaleX = { CurrentValue = 200f }, ScaleY = { CurrentValue = 200f } });

        // 子 transforms は順番に乗算され、Aggregate の初期値に左から掛けられる
        Matrix expected =
            new ScaleTransform { ScaleX = { CurrentValue = 200f }, ScaleY = { CurrentValue = 200f } }.CreateMatrix(Context)
            * new TranslateTransform(10f, 0f).CreateMatrix(Context);

        Assert.That(group.CreateMatrix(Context), Is.EqualTo(expected));
    }

    [Test]
    public void TransformGroup_DisabledChildIsIgnored()
    {
        var group = new TransformGroup();
        var child = new TranslateTransform(5f, 5f);
        child.IsEnabled = false;
        group.Children.Add(child);

        Assert.That(group.CreateMatrix(Context), Is.EqualTo(Matrix.Identity));
    }
}
