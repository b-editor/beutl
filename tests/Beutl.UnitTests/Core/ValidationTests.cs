using System.ComponentModel.DataAnnotations;
using Beutl.Validation;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl.UnitTests.Core;

public class ValidationTests
{
    private static ValidationContext EmptyContext => default;

    [Test]
    public void DataAnnotation_NullAttribute_ReturnsNull()
    {
        var validator = new DataAnnotationValidater<int>(null);
        Assert.That(validator.Validate(EmptyContext, 5), Is.Null);
    }

    [Test]
    public void DataAnnotation_RangeAttribute_ValidValueReturnsNull()
    {
        var validator = new DataAnnotationValidater<int>(new RangeAttribute(0, 100));
        Assert.That(validator.Validate(EmptyContext, 50), Is.Null);
    }

    [Test]
    public void DataAnnotation_RangeAttribute_InvalidValueReturnsErrorMessage()
    {
        var validator = new DataAnnotationValidater<int>(new RangeAttribute(0, 100));
        string? message = validator.Validate(EmptyContext, 500);
        Assert.That(message, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void DataAnnotation_TryCoerce_AlwaysReturnsFalse()
    {
        var validator = new DataAnnotationValidater<int>(new RangeAttribute(0, 100));
        int value = 500;
        Assert.Multiple(() =>
        {
            Assert.That(validator.TryCoerce(EmptyContext, ref value), Is.False);
            Assert.That(value, Is.EqualTo(500));
        });
    }

    [Test]
    public void RangeDataAnnotation_TryCoerce_ClampsToRange()
    {
        var validator = new RangeDataAnnotationValidater<int>(new RangeAttribute(0, 100));

        int low = -10;
        int high = 200;
        int inside = 50;

        Assert.Multiple(() =>
        {
            Assert.That(validator.TryCoerce(EmptyContext, ref low), Is.True);
            Assert.That(low, Is.EqualTo(0));
            Assert.That(validator.TryCoerce(EmptyContext, ref high), Is.True);
            Assert.That(high, Is.EqualTo(100));
            Assert.That(validator.TryCoerce(EmptyContext, ref inside), Is.True);
            Assert.That(inside, Is.EqualTo(50));
        });
    }

    [Test]
    public void RangeDataAnnotation_NullAttribute_ValidateReturnsNull_AndCoerceReturnsFalse()
    {
        var validator = new RangeDataAnnotationValidater<int> { Attribute = null };
        int value = 50;
        Assert.Multiple(() =>
        {
            Assert.That(validator.Validate(EmptyContext, 50), Is.Null);
            Assert.That(validator.TryCoerce(EmptyContext, ref value), Is.False);
        });
    }

    [Test]
    public void RangeDataAnnotation_DoubleAttribute_ProducesTypedBounds()
    {
        var validator = new RangeDataAnnotationValidater<float>(new RangeAttribute(-1.5, 1.5));

        float low = -10f;
        float high = 10f;
        Assert.Multiple(() =>
        {
            Assert.That(validator.Minimum, Is.EqualTo(-1.5f));
            Assert.That(validator.Maximum, Is.EqualTo(1.5f));
            validator.TryCoerce(EmptyContext, ref low);
            validator.TryCoerce(EmptyContext, ref high);
            Assert.That(low, Is.EqualTo(-1.5f));
            Assert.That(high, Is.EqualTo(1.5f));
        });
    }

    [Test]
    public void RangeDataAnnotation_Validate_OutOfRangeReturnsMessage()
    {
        var validator = new RangeDataAnnotationValidater<int>(new RangeAttribute(0, 100));
        string? message = validator.Validate(EmptyContext, 500);
        Assert.That(message, Is.Not.Null.And.Not.Empty);
        Assert.That(validator.Validate(EmptyContext, 50), Is.Null);
    }

    [Test]
    public void Multiple_TryCoerce_RunsAllInOrder()
    {
        var first = new RangeDataAnnotationValidater<int>(new RangeAttribute(-5, 5));
        var second = new RangeDataAnnotationValidater<int>(new RangeAttribute(0, 100));
        var validator = new MultipleValidator<int>([first, second]);

        int value = 200;
        Assert.That(validator.TryCoerce(EmptyContext, ref value), Is.True);
        Assert.That(value, Is.EqualTo(5));
    }

    [Test]
    public void Multiple_TryCoerce_StopsOnFailure()
    {
        var failing = new FailingCoerceValidator();
        var succeeding = new SuccessValidator();
        var validator = new MultipleValidator<int>([failing, succeeding]);

        int value = 5;
        Assert.Multiple(() =>
        {
            Assert.That(validator.TryCoerce(EmptyContext, ref value), Is.False);
            Assert.That(succeeding.CoerceCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Multiple_Validate_ConcatenatesAllMessages()
    {
        var first = new MessageValidator("err-1");
        var second = new MessageValidator("err-2");
        var validator = new MultipleValidator<int>([first, second]);

        string? combined = validator.Validate(EmptyContext, 0);
        Assert.That(combined, Does.Contain("err-1"));
        Assert.That(combined, Does.Contain("err-2"));
    }

    [Test]
    public void Multiple_Validate_NoErrorsReturnsNull()
    {
        var validator = new MultipleValidator<int>([
            new SuccessValidator(),
            new SuccessValidator(),
        ]);
        Assert.That(validator.Validate(EmptyContext, 0), Is.Null);
    }

    [Test]
    public void RangeDataAnnotation_BothExclusive_TryCoerceReturnsFalse()
    {
        var validator = new RangeDataAnnotationValidater<int>(
            new RangeAttribute(0, 100) { MinimumIsExclusive = true, MaximumIsExclusive = true }
        );

        int value = 50;
        Assert.That(validator.TryCoerce(EmptyContext, ref value), Is.False);
    }

    [Test]
    public void RangeDataAnnotation_MaxExclusiveOnly_OnlyClampsToMinimum()
    {
        var validator = new RangeDataAnnotationValidater<int>(
            new RangeAttribute(0, 100) { MaximumIsExclusive = true }
        );

        int low = -10;
        int high = 200;
        Assert.Multiple(() =>
        {
            Assert.That(validator.TryCoerce(EmptyContext, ref low), Is.True);
            Assert.That(low, Is.EqualTo(0));
            Assert.That(validator.TryCoerce(EmptyContext, ref high), Is.True);
            Assert.That(high, Is.EqualTo(200));
        });
    }

    [Test]
    public void RangeDataAnnotation_MinExclusiveOnly_OnlyClampsToMaximum()
    {
        var validator = new RangeDataAnnotationValidater<int>(
            new RangeAttribute(0, 100) { MinimumIsExclusive = true }
        );

        int low = -10;
        int high = 200;
        Assert.Multiple(() =>
        {
            Assert.That(validator.TryCoerce(EmptyContext, ref low), Is.True);
            Assert.That(low, Is.EqualTo(-10));
            Assert.That(validator.TryCoerce(EmptyContext, ref high), Is.True);
            Assert.That(high, Is.EqualTo(100));
        });
    }

    private sealed class FailingCoerceValidator : IValidator<int>
    {
        public bool TryCoerce(ValidationContext context, ref int value) => false;

        public string? Validate(ValidationContext context, int value) => null;
    }

    private sealed class SuccessValidator : IValidator<int>
    {
        public int CoerceCount { get; private set; }

        public bool TryCoerce(ValidationContext context, ref int value)
        {
            CoerceCount++;
            return true;
        }

        public string? Validate(ValidationContext context, int value) => null;
    }

    private sealed class MessageValidator(string message) : IValidator<int>
    {
        public bool TryCoerce(ValidationContext context, ref int value) => true;

        public string? Validate(ValidationContext context, int value) => message;
    }
}
