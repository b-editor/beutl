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
    public void TranslateTransform_FromVector_StoresValues()
    {
        var t = new TranslateTransform(new Vector(3f, 5f));
        Assert.Multiple(() =>
        {
            Assert.That(t.X.Value, Is.EqualTo(3f));
            Assert.That(t.Y.Value, Is.EqualTo(5f));
        });
    }

    [Test]
    public void TranslateTransform_FromPoint_StoresValues()
    {
        var t = new TranslateTransform(new Point(7f, -2f));
        Assert.Multiple(() =>
        {
            Assert.That(t.X.Value, Is.EqualTo(7f));
            Assert.That(t.Y.Value, Is.EqualTo(-2f));
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
    public void RotationTransform_FromRadians_ConvertsToDegrees()
    {
        RotationTransform t = RotationTransform.FromRadians(MathF.PI);
        Assert.That(t.Rotation.Value, Is.EqualTo(180f).Within(1e-3));
    }

    [Test]
    public void ScaleTransform_FromXY_AppliesPercentScale()
    {
        var t = new ScaleTransform(200f, 50f);
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
        var t = new ScaleTransform(100f, 100f, scale: 50f);
        Matrix m = t.CreateMatrix(Context);
        Assert.Multiple(() =>
        {
            Assert.That(m.M11, Is.EqualTo(0.5f).Within(1e-5));
            Assert.That(m.M22, Is.EqualTo(0.5f).Within(1e-5));
        });
    }

    [Test]
    public void ScaleTransform_FromVector_StoresValues()
    {
        var t = new ScaleTransform(new Vector(150f, 250f));
        Assert.Multiple(() =>
        {
            Assert.That(t.ScaleX.Value, Is.EqualTo(150f));
            Assert.That(t.ScaleY.Value, Is.EqualTo(250f));
        });
    }

    [Test]
    public void SkewTransform_FromDegrees_ProducesMatrix()
    {
        var t = new SkewTransform(45f, 0f);
        Matrix expected = Matrix.CreateSkew(MathUtilities.Deg2Rad(45f), 0f);
        Matrix actual = t.CreateMatrix(Context);
        Assert.That(actual.M21, Is.EqualTo(expected.M21).Within(1e-5));
    }

    [Test]
    public void SkewTransform_FromRadians_RoundtripsToDegrees()
    {
        SkewTransform t = SkewTransform.FromRadians(MathF.PI / 4f, 0f);
        Assert.That(t.SkewX.Value, Is.EqualTo(45f).Within(1e-3));
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
        var t = new MatrixTransform(matrix);
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
        group.Children.Add(new ScaleTransform(200f, 200f));

        // 子 transforms は順番に乗算され、Aggregate の初期値に左から掛けられる
        Matrix expected =
            new ScaleTransform(200f, 200f).CreateMatrix(Context)
            * new TranslateTransform(10f, 0f).CreateMatrix(Context);

        Assert.That(group.CreateMatrix(Context), Is.EqualTo(expected));
    }

    [Test]
    public void TransformGroup_DisabledChildIsIgnored()
    {
        var group = new TransformGroup();
        var child = new TranslateTransform(5f, 5f);
        child.IsEnabled.Value = false;
        group.Children.Add(child);

        Assert.That(group.CreateMatrix(Context), Is.EqualTo(Matrix.Identity));
    }

    // A transform on its own leaves its target where it is while disabled, as a disabled child of a group does (#4012).
    [Test]
    public void Transform_DisabledResourceAppliesNoMatrix()
    {
        var transform = new TranslateTransform(200f, 0f) { IsEnabled = { Value = false } };

        using var resource = transform.ToResource(Context);

        Assert.That(resource.Matrix, Is.EqualTo(Matrix.Identity));
    }

    [Test]
    public void Transform_EnabledAgainRestoresItsMatrixAndMovesTheVersion()
    {
        var transform = new TranslateTransform(200f, 0f) { IsEnabled = { Value = false } };
        using var resource = transform.ToResource(Context);
        int version = resource.Version;

        transform.IsEnabled.Value = true;
        bool versionBumped = false;
        resource.Reconcile(transform, Context, ref versionBumped);

        Assert.Multiple(() =>
        {
            Assert.That(resource.Matrix, Is.EqualTo(Matrix.CreateTranslation(200f, 0f)));
            Assert.That(resource.Version, Is.GreaterThan(version));
        });
    }

    // A group at the root is a transform like any other.
    [Test]
    public void TransformGroup_DisabledResourceAppliesNoMatrix()
    {
        var group = new TransformGroup { IsEnabled = { Value = false } };
        group.Children.Add(new TranslateTransform(200f, 0f));

        using var resource = group.ToResource(Context);

        Assert.That(resource.Matrix, Is.EqualTo(Matrix.Identity));
    }

    // A presenter follows its target's switch, as a drawable's and a filter effect's presenters do.
    [Test]
    public void TransformPresenter_DisabledTargetAppliesNoMatrix()
    {
        var target = new TranslateTransform(200f, 0f);
        var presenter = new TransformPresenter();
        presenter.Target.Value = target;
        Matrix enabled = presenter.CreateMatrix(Context);

        target.IsEnabled.Value = false;

        Assert.Multiple(() =>
        {
            Assert.That(enabled, Is.EqualTo(Matrix.CreateTranslation(200f, 0f)));
            Assert.That(presenter.CreateMatrix(Context), Is.EqualTo(Matrix.Identity));
        });
    }
}
