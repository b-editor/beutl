using System.Numerics;

using Beutl.Animation.Animators;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Animation;

[TestFixture]
public class AnimatorInterpolateTests
{
    [Test]
    [TestCase(0f, 1f, 0f, 0f)]
    [TestCase(0f, 1f, 1f, 1f)]
    [TestCase(0f, 1f, 0.5f, 0.5f)]
    [TestCase(2f, 4f, 0.25f, 2.5f)]
    public void FloatAnimator_Linear(float oldVal, float newVal, float progress, float expected)
    {
        var animator = new FloatAnimator();
        Assert.That(animator.Interpolate(progress, oldVal, newVal), Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    [TestCase(0d, 10d, 0.5f, 5d)]
    [TestCase(-1d, 1d, 0.5f, 0d)]
    public void DoubleAnimator_Linear(double oldVal, double newVal, float progress, double expected)
    {
        var animator = new DoubleAnimator();
        Assert.That(animator.Interpolate(progress, oldVal, newVal), Is.EqualTo(expected).Within(1e-6));
    }

    [Test]
    public void DecimalAnimator_Midpoint()
    {
        var animator = new DecimalAnimator();
        Assert.That(animator.Interpolate(0.5f, 0m, 10m), Is.EqualTo(5m));
    }

    [Test]
    [TestCase(0, 100, 0f, 0)]
    [TestCase(0, 100, 0.5f, 50)]
    [TestCase(0, 100, 1f, 100)]
    public void Int32Animator_Linear(int oldVal, int newVal, float progress, int expected)
    {
        var animator = new Int32Animator();
        Assert.That(animator.Interpolate(progress, oldVal, newVal), Is.EqualTo(expected));
    }

    [Test]
    public void Int16Animator_Midpoint()
    {
        var animator = new Int16Animator();
        Assert.That(animator.Interpolate(0.5f, (short)0, (short)100), Is.EqualTo((short)50));
    }

    [Test]
    public void Int64Animator_Midpoint()
    {
        var animator = new Int64Animator();
        Assert.That(animator.Interpolate(0.5f, 0L, 1000L), Is.EqualTo(500L));
    }

    [Test]
    public void ByteAnimator_Midpoint()
    {
        var animator = new ByteAnimator();
        Assert.That(animator.Interpolate(0.5f, (byte)0, (byte)200), Is.EqualTo((byte)100));
    }

    [Test]
    public void SByteAnimator_Midpoint()
    {
        var animator = new SByteAnimator();
        Assert.That(animator.Interpolate(0.5f, (sbyte)-100, (sbyte)100), Is.EqualTo((sbyte)0));
    }

    [Test]
    public void UInt16Animator_Midpoint()
    {
        var animator = new UInt16Animator();
        Assert.That(animator.Interpolate(0.5f, (ushort)0, (ushort)200), Is.EqualTo((ushort)100));
    }

    [Test]
    public void UInt32Animator_Midpoint()
    {
        var animator = new UInt32Animator();
        Assert.That(animator.Interpolate(0.5f, 0u, 200u), Is.EqualTo(100u));
    }

    [Test]
    public void UInt64Animator_Midpoint()
    {
        var animator = new UInt64Animator();
        Assert.That(animator.Interpolate(0.5f, 0ul, 200ul), Is.EqualTo(100ul));
    }

    [Test]
    public void Int32Animator_DoesNotOverflowAtMaxValue()
    {
        var animator = new Int32Animator();
        Assert.That(animator.Interpolate(1f, 0, int.MaxValue), Is.EqualTo(int.MaxValue));
        Assert.That(animator.Interpolate(0f, int.MaxValue, 0), Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void Int32Animator_FullRangeMidpoint()
    {
        var animator = new Int32Animator();
        // double 補間: -2^31 + (2^32 - 1) * 0.5 = -0.5 → Math.Round(AwayFromZero) → -1
        Assert.That(animator.Interpolate(0.5f, int.MinValue, int.MaxValue), Is.EqualTo(-1));
    }

    [Test]
    public void Int32Animator_RoundsMidpointAwayFromZero()
    {
        var animator = new Int32Animator();
        // MidpointRounding.AwayFromZero: 0.5 → 1 (ToEven なら 0)
        Assert.That(animator.Interpolate(0.5f, 0, 1), Is.EqualTo(1));
        // MidpointRounding.AwayFromZero: -0.5 → -1 (ToEven なら 0)
        Assert.That(animator.Interpolate(0.5f, -1, 0), Is.EqualTo(-1));
    }

    [Test]
    public void Int64Animator_DoesNotOverflowAtMaxValue()
    {
        var animator = new Int64Animator();
        Assert.That(animator.Interpolate(1f, 0L, long.MaxValue), Is.EqualTo(long.MaxValue));
        Assert.That(animator.Interpolate(0f, long.MaxValue, 0L), Is.EqualTo(long.MaxValue));
    }

    [Test]
    public void Int64Animator_PreservesPrecisionForLargeValues()
    {
        var animator = new Int64Animator();
        // 旧実装は (float)long.MaxValue で正規化するため、結果が float の精度に丸まり
        // 入力の絶対値とは無関係に誤差が乗る (この入力では 1024 ずれて 49_999_998_976L を返す)。
        // 新実装の double 補間では完全に一致する。
        Assert.That(animator.Interpolate(0.5f, 0L, 100_000_000_000L), Is.EqualTo(50_000_000_000L));
    }

    [Test]
    public void UInt32Animator_DoesNotOverflowAtMaxValue()
    {
        var animator = new UInt32Animator();
        Assert.That(animator.Interpolate(1f, 0u, uint.MaxValue), Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void UInt64Animator_DoesNotOverflowAtMaxValue()
    {
        var animator = new UInt64Animator();
        Assert.That(animator.Interpolate(1f, 0ul, ulong.MaxValue), Is.EqualTo(ulong.MaxValue));
    }

    [Test]
    public void UInt64Animator_DoesNotUnderflowOnReverseInterpolation()
    {
        var animator = new UInt64Animator();
        // newValue < oldValue でも ulong 直接減算による underflow を起こさず、
        // 中点近傍の値を返すこと (double 精度の許容差あり)。
        var result = animator.Interpolate(0.5f, ulong.MaxValue, 0ul);
        const ulong expected = ulong.MaxValue / 2;
        // double の有効精度は 53bit (仮数 52bit + 暗黙の先頭 1bit)。
        // ulong.MaxValue 近傍の 1 ULP は 2^(64-53) = 2048 なので 2 ULP 分の許容差。
        Assert.That(result, Is.GreaterThan(expected - 4096ul).And.LessThan(expected + 4096ul));
    }

    [Test]
    public void Int32Animator_ClampsOnRangeOvershoot()
    {
        var animator = new Int32Animator();
        // progress > 1 (Back/Elastic 系イージング由来の overshoot) で int.MaxValue を超える
        Assert.That(animator.Interpolate(2f, 0, int.MaxValue), Is.EqualTo(int.MaxValue));
        // progress < 0 で int.MinValue を下回る
        Assert.That(animator.Interpolate(-2f, 0, int.MaxValue), Is.EqualTo(int.MinValue));
    }

    [Test]
    public void Int32Animator_ReverseFullRange()
    {
        var animator = new Int32Animator();
        // 逆方向フルレンジの境界 (progress=1) で newValue に到達することを確認。
        Assert.That(animator.Interpolate(1f, int.MaxValue, int.MinValue), Is.EqualTo(int.MinValue));
    }

    [Test]
    public void Int64Animator_ClampsOnRangeOvershoot()
    {
        var animator = new Int64Animator();
        Assert.That(animator.Interpolate(2f, 0L, long.MaxValue), Is.EqualTo(long.MaxValue));
        Assert.That(animator.Interpolate(-2f, 0L, long.MaxValue), Is.EqualTo(long.MinValue));
    }

    [Test]
    public void Int64Animator_ReverseFullRange()
    {
        var animator = new Int64Animator();
        Assert.That(animator.Interpolate(1f, long.MaxValue, long.MinValue), Is.EqualTo(long.MinValue));
    }

    [Test]
    public void UInt32Animator_ClampsOnRangeOvershoot()
    {
        var animator = new UInt32Animator();
        // progress < 0 で v が負になっても Clamp で 0
        Assert.That(animator.Interpolate(-2f, 100u, 200u), Is.EqualTo(0u));
        Assert.That(animator.Interpolate(2f, 0u, uint.MaxValue), Is.EqualTo(uint.MaxValue));
    }

    [Test]
    public void UInt64Animator_ClampsOnRangeOvershoot()
    {
        var animator = new UInt64Animator();
        Assert.That(animator.Interpolate(-2f, 100ul, 200ul), Is.EqualTo(0ul));
        Assert.That(animator.Interpolate(2f, 0ul, ulong.MaxValue), Is.EqualTo(ulong.MaxValue));
    }

    [Test]
    public void Int64Animator_PreservesExactEndpointNearMaxValue()
    {
        var animator = new Int64Animator();
        // double 経由では long.MaxValue - 1 が 2^63 に丸まり long.MaxValue として扱われる。
        // progress=0/1 では oldValue/newValue を正確に返すこと。
        Assert.That(animator.Interpolate(0f, long.MaxValue - 1, 0L), Is.EqualTo(long.MaxValue - 1));
        Assert.That(animator.Interpolate(1f, 0L, long.MaxValue - 1), Is.EqualTo(long.MaxValue - 1));
        Assert.That(animator.Interpolate(0f, long.MinValue + 1, 0L), Is.EqualTo(long.MinValue + 1));
    }

    [Test]
    public void UInt64Animator_PreservesExactEndpointNearMaxValue()
    {
        var animator = new UInt64Animator();
        Assert.That(animator.Interpolate(0f, ulong.MaxValue - 1, 0ul), Is.EqualTo(ulong.MaxValue - 1));
        Assert.That(animator.Interpolate(1f, 0ul, ulong.MaxValue - 1), Is.EqualTo(ulong.MaxValue - 1));
    }

    [Test]
    [TestCase(0f, false, false, false)]
    [TestCase(0.49f, false, true, false)]
    [TestCase(0.5f, false, true, false)]
    [TestCase(0.99f, false, true, false)]
    [TestCase(1f, false, true, true)]
    public void BoolAnimator_StepAtEnd(float progress, bool oldVal, bool newVal, bool expected)
    {
        var animator = new BoolAnimator();
        Assert.That(animator.Interpolate(progress, oldVal, newVal), Is.EqualTo(expected));
    }

    [Test]
    public void ColorAnimator_BoundaryReturnsEndpoints()
    {
        var animator = new ColorAnimator();
        var c1 = Color.FromArgb(0, 0, 0, 0);
        var c2 = Color.FromArgb(255, 255, 255, 255);

        Assert.That(animator.Interpolate(0f, c1, c2), Is.EqualTo(c1));
        Assert.That(animator.Interpolate(1f, c1, c2), Is.EqualTo(c2));
    }

    [Test]
    public void ColorAnimator_AlphaInterpolation()
    {
        var animator = new ColorAnimator();
        var c1 = Color.FromArgb(0, 0, 0, 0);
        var c2 = Color.FromArgb(200, 0, 0, 0);

        var mid = animator.Interpolate(0.5f, c1, c2);
        Assert.That(mid.A, Is.EqualTo(100));
    }

    [Test]
    public void PointAnimator_Linear()
    {
        var animator = new PointAnimator();
        var result = animator.Interpolate(0.5f, new Point(0, 0), new Point(10, 20));
        Assert.That(result.X, Is.EqualTo(5f).Within(1e-6));
        Assert.That(result.Y, Is.EqualTo(10f).Within(1e-6));
    }

    [Test]
    public void SizeAnimator_Linear()
    {
        var animator = new SizeAnimator();
        var result = animator.Interpolate(0.25f, new Size(0, 0), new Size(40, 80));
        Assert.That(result.Width, Is.EqualTo(10f).Within(1e-6));
        Assert.That(result.Height, Is.EqualTo(20f).Within(1e-6));
    }

    [Test]
    public void VectorAnimator_Linear()
    {
        var animator = new VectorAnimator();
        var result = animator.Interpolate(0.5f, new Beutl.Graphics.Vector(0, 0), new Beutl.Graphics.Vector(10, 10));
        Assert.That(result.X, Is.EqualTo(5f).Within(1e-6));
        Assert.That(result.Y, Is.EqualTo(5f).Within(1e-6));
    }

    [Test]
    public void RectAnimator_Linear()
    {
        var animator = new RectAnimator();
        var result = animator.Interpolate(0.5f, new Rect(0, 0, 0, 0), new Rect(10, 20, 30, 40));
        Assert.That(result.X, Is.EqualTo(5f).Within(1e-6));
        Assert.That(result.Y, Is.EqualTo(10f).Within(1e-6));
        Assert.That(result.Width, Is.EqualTo(15f).Within(1e-6));
        Assert.That(result.Height, Is.EqualTo(20f).Within(1e-6));
    }

    [Test]
    public void ThicknessAnimator_Linear()
    {
        var animator = new ThicknessAnimator();
        var result = animator.Interpolate(0.5f, new Thickness(0), new Thickness(10, 20, 30, 40));
        Assert.That(result.Left, Is.EqualTo(5f).Within(1e-6));
        Assert.That(result.Top, Is.EqualTo(10f).Within(1e-6));
        Assert.That(result.Right, Is.EqualTo(15f).Within(1e-6));
        Assert.That(result.Bottom, Is.EqualTo(20f).Within(1e-6));
    }

    [Test]
    public void CornerRadiusAnimator_Linear()
    {
        var animator = new CornerRadiusAnimator();
        var result = animator.Interpolate(0.5f, new CornerRadius(0), new CornerRadius(10, 20, 30, 40));
        Assert.That(result.TopLeft, Is.EqualTo(5f).Within(1e-6));
        Assert.That(result.TopRight, Is.EqualTo(10f).Within(1e-6));
        Assert.That(result.BottomRight, Is.EqualTo(15f).Within(1e-6));
        Assert.That(result.BottomLeft, Is.EqualTo(20f).Within(1e-6));
    }

    [Test]
    public void PixelPointAnimator_LinearIntegerInterpolation()
    {
        var animator = new PixelPointAnimator();
        var result = animator.Interpolate(0.5f, new PixelPoint(0, 0), new PixelPoint(10, 20));
        Assert.That(result.X, Is.EqualTo(5));
        Assert.That(result.Y, Is.EqualTo(10));
    }

    [Test]
    public void PixelSizeAnimator_LinearIntegerInterpolation()
    {
        var animator = new PixelSizeAnimator();
        var result = animator.Interpolate(0.5f, new PixelSize(0, 0), new PixelSize(10, 20));
        Assert.That(result.Width, Is.EqualTo(5));
        Assert.That(result.Height, Is.EqualTo(10));
    }

    [Test]
    public void PixelRectAnimator_LinearIntegerInterpolation()
    {
        var animator = new PixelRectAnimator();
        var result = animator.Interpolate(0.5f, new PixelRect(0, 0, 0, 0), new PixelRect(10, 20, 30, 40));
        Assert.That(result.X, Is.EqualTo(5));
        Assert.That(result.Y, Is.EqualTo(10));
        Assert.That(result.Width, Is.EqualTo(15));
        Assert.That(result.Height, Is.EqualTo(20));
    }

    [Test]
    public void RelativePointAnimator_SameUnitInterpolates()
    {
        var animator = new RelativePointAnimator();
        var result = animator.Interpolate(
            0.5f,
            new RelativePoint(0f, 0f, RelativeUnit.Relative),
            new RelativePoint(1f, 1f, RelativeUnit.Relative));
        Assert.That(result.Unit, Is.EqualTo(RelativeUnit.Relative));
        Assert.That(result.Point.X, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(result.Point.Y, Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void RelativePointAnimator_DifferentUnit_StepAtHalf()
    {
        var animator = new RelativePointAnimator();
        var oldP = new RelativePoint(0f, 0f, RelativeUnit.Absolute);
        var newP = new RelativePoint(1f, 1f, RelativeUnit.Relative);

        Assert.That(animator.Interpolate(0.4f, oldP, newP), Is.EqualTo(oldP));
        Assert.That(animator.Interpolate(0.5f, oldP, newP), Is.EqualTo(newP));
        Assert.That(animator.Interpolate(0.6f, oldP, newP), Is.EqualTo(newP));
    }

    [Test]
    public void RelativeRectAnimator_SameUnitInterpolates()
    {
        var animator = new RelativeRectAnimator();
        var result = animator.Interpolate(
            0.5f,
            new RelativeRect(0, 0, 0, 0, RelativeUnit.Relative),
            new RelativeRect(1, 1, 1, 1, RelativeUnit.Relative));
        Assert.That(result.Unit, Is.EqualTo(RelativeUnit.Relative));
        Assert.That(result.Rect.X, Is.EqualTo(0.5f).Within(1e-6));
        Assert.That(result.Rect.Width, Is.EqualTo(0.5f).Within(1e-6));
    }

    [Test]
    public void RelativeRectAnimator_DifferentUnit_StepAtHalf()
    {
        var animator = new RelativeRectAnimator();
        var oldR = new RelativeRect(0, 0, 0, 0, RelativeUnit.Absolute);
        var newR = new RelativeRect(1, 1, 1, 1, RelativeUnit.Relative);

        Assert.That(animator.Interpolate(0.49f, oldR, newR), Is.EqualTo(oldR));
        Assert.That(animator.Interpolate(0.5f, oldR, newR), Is.EqualTo(newR));
    }

    [Test]
    public void Vector2Animator_Linear()
    {
        var animator = new Vector2Animator();
        var result = animator.Interpolate(0.5f, new Vector2(0, 0), new Vector2(10, 20));
        Assert.That(result.X, Is.EqualTo(5f).Within(1e-6));
        Assert.That(result.Y, Is.EqualTo(10f).Within(1e-6));
    }

    [Test]
    public void Vector3Animator_Linear()
    {
        var animator = new Vector3Animator();
        var result = animator.Interpolate(0.5f, new Vector3(0, 0, 0), new Vector3(10, 20, 30));
        Assert.That(result.X, Is.EqualTo(5f).Within(1e-6));
        Assert.That(result.Z, Is.EqualTo(15f).Within(1e-6));
    }

    [Test]
    public void Vector4Animator_Linear()
    {
        var animator = new Vector4Animator();
        var result = animator.Interpolate(0.5f, new Vector4(0, 0, 0, 0), new Vector4(2, 4, 6, 8));
        Assert.That(result.W, Is.EqualTo(4f).Within(1e-6));
    }

    [Test]
    public void Matrix3x2Animator_Linear()
    {
        var animator = new Matrix3x2Animator();
        var oldM = Matrix3x2.Identity;
        var newM = new Matrix3x2(2, 0, 0, 2, 10, 20);
        var result = animator.Interpolate(0.5f, oldM, newM);
        Assert.That(result.M11, Is.EqualTo(1.5f).Within(1e-6));
        Assert.That(result.M31, Is.EqualTo(5f).Within(1e-6));
    }

    [Test]
    public void Matrix4x4Animator_Linear()
    {
        var animator = new Matrix4x4Animator();
        var oldM = Matrix4x4.Identity;
        var newM = Matrix4x4.Identity;
        newM.M11 = 2;
        var result = animator.Interpolate(0.5f, oldM, newM);
        Assert.That(result.M11, Is.EqualTo(1.5f).Within(1e-6));
    }

    [Test]
    public void MatrixAnimator_Linear()
    {
        var animator = new MatrixAnimator();
        var oldM = Matrix.Identity;
        var newM = new Matrix(2, 0, 0, 2, 10, 20);
        var result = animator.Interpolate(0.5f, oldM, newM);
        Assert.That(result.M11, Is.EqualTo(1.5f).Within(1e-6));
        Assert.That(result.M31, Is.EqualTo(5f).Within(1e-6));
    }

    [Test]
    public void GradingColorAnimator_Linear()
    {
        var animator = new GradingColorAnimator();
        var oldC = new GradingColor(0, 0, 0);
        var newC = new GradingColor(2, 4, 6);
        var result = animator.Interpolate(0.5f, oldC, newC);
        Assert.That(result.R, Is.EqualTo(1f).Within(1e-6));
        Assert.That(result.G, Is.EqualTo(2f).Within(1e-6));
        Assert.That(result.B, Is.EqualTo(3f).Within(1e-6));
    }

    [Test]
    public void Animator_DefaultValueReturnsDefault()
    {
        var animator = new FloatAnimator();
        Assert.That(animator.DefaultValue(), Is.EqualTo(0f));

        var colorAnim = new ColorAnimator();
        Assert.That(colorAnim.DefaultValue(), Is.EqualTo(default(Color)));
    }
}
