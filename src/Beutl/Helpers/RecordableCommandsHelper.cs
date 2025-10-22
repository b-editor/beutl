using Beutl.Engine;

namespace Beutl;

public static class RecordableCommandsHelper
{
    extension(RecordableCommands)
    {
        public static IRecordableCommand Edit<T>(
            IProperty<T> property,
            T? value,
            Optional<T?> oldValue = default)
        {
            var actualOLdValue = oldValue.HasValue ? oldValue.Value : property.CurrentValue;
            return RecordableCommands.Create(
                () => property.CurrentValue = value!,
                () => property.CurrentValue = actualOLdValue!,
                []);
        }

        public static IRecordableCommand Edit<T>(
            IPropertyAdapter<T> property,
            T? value,
            Optional<T?> oldValue = default)
        {
            var actualOLdValue = oldValue.HasValue ? oldValue.Value : property.GetValue();
            return RecordableCommands.Create(
                () => property.SetValue(value),
                () => property.SetValue(actualOLdValue),
                []);
        }
    }
}
