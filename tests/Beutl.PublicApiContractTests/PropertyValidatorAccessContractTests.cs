using Beutl.Engine;
using Beutl.Validation;
using ValidationContext = Beutl.Validation.ValidationContext;

namespace Beutl.PublicApiContractTests;

[TestFixture]
public sealed class PropertyValidatorAccessContractTests
{
    [Test]
    public void PublicPropertiesExposeTheValidatorTheyActuallyUse()
    {
        var validator = new PassthroughValidator();
        IExpressionProperty<int> simple = Property.CreateExpression(0, validator);
        IExpressionProperty<int> animatable = Property.CreateAnimatable(0, validator);
        IListProperty<int> list = Property.CreateList<int>();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(simple.GetValidator(), Is.SameAs(validator));
            Assert.That(animatable.GetValidator(), Is.SameAs(validator));
            Assert.That(list.GetValidator(), Is.Null);
        }
    }

    private sealed class PassthroughValidator : IValidator<int>
    {
        public bool TryCoerce(ValidationContext context, ref int value) => true;

        public string? Validate(ValidationContext context, int value) => null;
    }
}
