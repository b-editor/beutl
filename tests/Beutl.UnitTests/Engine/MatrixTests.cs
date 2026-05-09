using Beutl.Graphics;

namespace Beutl.UnitTests.Engine;

public class MatrixTests
{
    [Test]
    public void Identity_IsIdentityMatrix()
    {
        Matrix id = Matrix.Identity;
        Assert.That(id.IsIdentity, Is.True);
        Assert.That(id.M11, Is.EqualTo(1));
        Assert.That(id.M22, Is.EqualTo(1));
        Assert.That(id.M33, Is.EqualTo(1));
        Assert.That(id.M12, Is.EqualTo(0));
    }

    [Test]
    public void CreateTranslation_AppliesOffset()
    {
        Matrix t = Matrix.CreateTranslation(5, 10);
        Point p = t.Transform(new Point(1, 2));
        Assert.That(p, Is.EqualTo(new Point(6, 12)));
    }

    [Test]
    public void CreateTranslation_FromVector_Equivalent()
    {
        Matrix vec = Matrix.CreateTranslation(new Vector(3, 4));
        Matrix scalar = Matrix.CreateTranslation(3, 4);
        Assert.That(vec, Is.EqualTo(scalar));
    }

    [Test]
    public void CreateScale_DoublesPointDistance()
    {
        Matrix s = Matrix.CreateScale(2, 3);
        Assert.That(s.Transform(new Point(1, 1)), Is.EqualTo(new Point(2, 3)));

        Matrix sv = Matrix.CreateScale(new Vector(2, 3));
        Assert.That(sv, Is.EqualTo(s));
    }

    [Test]
    public void CreateRotation_RotatesUnitVectors()
    {
        Matrix r = Matrix.CreateRotation(MathF.PI / 2f);
        Point rotated = r.Transform(new Point(1, 0));
        Assert.That(rotated.X, Is.EqualTo(0).Within(1e-5f));
        Assert.That(rotated.Y, Is.EqualTo(1).Within(1e-5f));
    }

    [Test]
    public void CreateSkew_PerformsShear()
    {
        Matrix sk = Matrix.CreateSkew(MathF.PI / 4f, 0);
        Point p = sk.Transform(new Point(0, 1));
        Assert.That(p.X, Is.EqualTo(1).Within(1e-5f));
        Assert.That(p.Y, Is.EqualTo(1).Within(1e-5f));
    }

    [Test]
    public void Multiplication_Composes_AppendAndPrepend()
    {
        Matrix a = Matrix.CreateTranslation(1, 0);
        Matrix b = Matrix.CreateTranslation(0, 1);

        Matrix appended = a.Append(b);
        Matrix prepended = a.Prepend(b);

        Assert.That(appended, Is.EqualTo(a * b));
        Assert.That(prepended, Is.EqualTo(b * a));
    }

    [Test]
    public void GetDeterminant_Identity_IsOne()
    {
        Assert.That(Matrix.Identity.GetDeterminant(), Is.EqualTo(1f));
    }

    [Test]
    public void HasInverse_True_ForIdentity_AndScale()
    {
        Assert.That(Matrix.Identity.HasInverse, Is.True);
        Assert.That(Matrix.CreateScale(2, 2).HasInverse, Is.True);
    }

    [Test]
    public void HasInverse_False_ForZeroDeterminant()
    {
        var degenerate = new Matrix(0, 0, 0, 0, 0, 0);
        Assert.That(degenerate.HasInverse, Is.False);
        Assert.That(degenerate.TryInvert(out _), Is.False);
        Assert.That(() => degenerate.Invert(), Throws.InvalidOperationException);
    }

    [Test]
    public void Invert_RoundTripsThroughIdentity()
    {
        Matrix t = Matrix.CreateTranslation(5, 10) * Matrix.CreateScale(2, 4);
        Matrix inv = t.Invert();
        Matrix product = t * inv;

        Assert.That(product.M11, Is.EqualTo(1).Within(1e-4f));
        Assert.That(product.M22, Is.EqualTo(1).Within(1e-4f));
        Assert.That(product.M31, Is.EqualTo(0).Within(1e-4f));
        Assert.That(product.M32, Is.EqualTo(0).Within(1e-4f));
    }

    [Test]
    public void NegationOperator_ReturnsInverse()
    {
        Matrix t = Matrix.CreateTranslation(2, 3);
        Matrix inv = -t;
        Assert.That((t * inv).M31, Is.EqualTo(0).Within(1e-5f));
        Assert.That((t * inv).M32, Is.EqualTo(0).Within(1e-5f));
    }

    [Test]
    public void Equality_ComparesAllNine()
    {
        Matrix a = Matrix.CreateScale(2, 3);
        Matrix b = Matrix.CreateScale(2, 3);
        Matrix c = Matrix.CreateScale(2, 4);

        Assert.That(a == b, Is.True);
        Assert.That(a != c, Is.True);
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)"x"), Is.False);
        Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
    }

    [Test]
    public void TryDecompose_AffineMatrix_ReturnsTrue()
    {
        Matrix m = Matrix.CreateScale(2, 3) * Matrix.CreateTranslation(5, 6);

        Assert.That(m.TryDecomposeTransform(out Vector translate, out Vector scale, out _, out _), Is.True);
        Assert.That(translate.X, Is.EqualTo(5).Within(1e-4f));
        Assert.That(translate.Y, Is.EqualTo(6).Within(1e-4f));
        Assert.That(scale.X, Is.EqualTo(2).Within(1e-4f));
        Assert.That(scale.Y, Is.EqualTo(3).Within(1e-4f));
    }

    [Test]
    public void TryDecompose_PerspectiveMatrix_ReturnsFalse()
    {
        var perspective = new Matrix(1, 0, 0.01f, 0, 1, 0, 0, 0, 1);
        Assert.That(perspective.TryDecomposeTransform(out _, out _, out _, out _), Is.False);
    }

    [Test]
    public void ComposeTransform_RoundTripsTranslation()
    {
        Matrix m = Matrix.ComposeTransform(new Vector(5, 6), Vector.One, Vector.Zero, 0);
        Assert.That(m.M31, Is.EqualTo(5).Within(1e-5f));
        Assert.That(m.M32, Is.EqualTo(6).Within(1e-5f));
    }

    [Test]
    public void ToString_NonPerspective_UsesShortForm()
    {
        Matrix m = Matrix.CreateTranslation(2, 3);
        string s = m.ToString();
        Assert.That(s, Does.StartWith("matrix("));
        Assert.That(s.Split(',').Length, Is.EqualTo(6));
    }

    [Test]
    public void ToString_Perspective_UsesNineValueForm()
    {
        var m = new Matrix(1, 0, 0.5f, 0, 1, 0, 0, 0, 1);
        string s = m.ToString();
        Assert.That(s.Split(',').Length, Is.EqualTo(9));
    }

    [Test]
    public void Parse_NonPerspective_FromString()
    {
        Matrix m = Matrix.Parse("matrix(1, 0, 0, 1, 5, 6)");
        Assert.That(m, Is.EqualTo(Matrix.CreateTranslation(5, 6)));
    }

    [Test]
    public void TryParse_InvalidString_ReturnsFalse()
    {
        Assert.That(Matrix.TryParse("not a matrix", out _), Is.False);
    }
}
