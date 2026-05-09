namespace Beutl.UnitTests.Core;

public class OptionalTests
{
    [Test]
    public void Default_HasValue_IsFalse()
    {
        Optional<int> empty = default;
        Assert.That(empty.HasValue, Is.False);
        Assert.That(Optional<int>.Empty.HasValue, Is.False);
    }

    [Test]
    public void Construct_WithValue_HasValueIsTrue()
    {
        var opt = new Optional<int>(42);
        Assert.That(opt.HasValue, Is.True);
        Assert.That(opt.Value, Is.EqualTo(42));
    }

    [Test]
    public void Value_OnEmpty_Throws()
    {
        Optional<int> empty = default;
        Assert.That(() => empty.Value, Throws.InvalidOperationException);
    }

    [Test]
    public void GetValueOrDefault_NoArgs_ReturnsDefault()
    {
        Optional<int> empty = default;
        Assert.That(empty.GetValueOrDefault(), Is.EqualTo(0));

        var opt = new Optional<int>(7);
        Assert.That(opt.GetValueOrDefault(), Is.EqualTo(7));
    }

    [Test]
    public void GetValueOrDefault_WithFallback_ReturnsFallbackWhenEmpty()
    {
        Optional<int> empty = default;
        Assert.That(empty.GetValueOrDefault(99), Is.EqualTo(99));

        var opt = new Optional<int>(7);
        Assert.That(opt.GetValueOrDefault(99), Is.EqualTo(7));
    }

    [Test]
    public void GetValueOrDefault_TypedCast_WorksForCompatibleType()
    {
        var opt = new Optional<object>("text");
        Assert.That(opt.GetValueOrDefault<string>(), Is.EqualTo("text"));

        var optInt = new Optional<object>(123);
        Assert.That(optInt.GetValueOrDefault<string>(), Is.Null);
    }

    [Test]
    public void GetValueOrDefault_TypedCastWithFallback_UsesFallbackWhenEmpty()
    {
        // Type mismatch when HasValue still returns default(TResult) by current implementation.
        var opt = new Optional<object>(123);
        Assert.That(opt.GetValueOrDefault<string>("fallback"), Is.Null);

        Optional<object> empty = default;
        Assert.That(empty.GetValueOrDefault<string>("fallback"), Is.EqualTo("fallback"));
    }

    [Test]
    public void Equality_TwoEmpty_AreEqual()
    {
        Optional<int> a = default;
        Optional<int> b = default;
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a == b, Is.True);
        Assert.That(a != b, Is.False);
    }

    [Test]
    public void Equality_EmptyVsValue_AreNotEqual()
    {
        Optional<int> empty = default;
        var withValue = new Optional<int>(0);
        Assert.That(empty == withValue, Is.False);
        Assert.That(empty != withValue, Is.True);
    }

    [Test]
    public void Equality_SameValues_AreEqual()
    {
        var a = new Optional<string>("hi");
        var b = new Optional<string>("hi");
        Assert.That(a, Is.EqualTo(b));
        Assert.That(a.Equals((object)b), Is.True);
        Assert.That(a.Equals((object)42), Is.False);
    }

    [Test]
    public void GetHashCode_EmptyIsZero_WithValueIsValueHash()
    {
        Assert.That(Optional<int>.Empty.GetHashCode(), Is.EqualTo(0));
        Assert.That(new Optional<int>(7).GetHashCode(), Is.EqualTo(7.GetHashCode()));
        Assert.That(new Optional<string?>(null).GetHashCode(), Is.EqualTo(0));
    }

    [Test]
    public void ToString_RepresentsState()
    {
        Assert.That(Optional<int>.Empty.ToString(), Is.EqualTo("(empty)"));
        Assert.That(new Optional<string?>(null).ToString(), Is.EqualTo("(null)"));
        Assert.That(new Optional<int>(5).ToString(), Is.EqualTo("5"));
    }

    [Test]
    public void ToObject_PreservesEmptiness()
    {
        Assert.That(Optional<int>.Empty.ToObject().HasValue, Is.False);

        Optional<object?> obj = new Optional<int>(3).ToObject();
        Assert.That(obj.HasValue, Is.True);
        Assert.That(obj.Value, Is.EqualTo(3));
    }

    [Test]
    public void ImplicitConversion_FromValue_HasValueIsTrue()
    {
        Optional<int> opt = 5;
        Assert.That(opt.HasValue, Is.True);
        Assert.That(opt.Value, Is.EqualTo(5));
    }

    [Test]
    public void IOptional_GetValueType_ReturnsT()
    {
        IOptional opt = new Optional<long>(1);
        Assert.That(opt.GetValueType(), Is.EqualTo(typeof(long)));
    }
}
