using System.ComponentModel.DataAnnotations;
using Beutl.Validation;

namespace Beutl.Engine;

// The engine's members of Property, which code importing Beutl.Engine calls as Property.CreateAnimatable.
public static class EnginePropertyExtensions
{
    extension(Property)
    {
        public static IExpressionProperty<T> CreateAnimatable<T>(
            T defaultValue = default(T)!,
            IValidator<T>? validator = null)
        {
            return new AnimatableProperty<T>(defaultValue, validator);
        }

        public static IExpressionProperty<T> CreateAnimatable<T>(
            T defaultValue,
            params ValidationAttribute[] validationAttributes)
        {
            var validator = validationAttributes.Length > 0
                ? new MultipleValidator<T>(PropertyValidators.FromAttributes<T>(validationAttributes))
                : null;

            return Property.CreateAnimatable(defaultValue, validator);
        }
    }
}
