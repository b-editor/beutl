using Avalonia.Controls;

using Beutl.Language;

namespace Beutl.Controls.PropertyEditors;

internal static class DataValidationMessages
{
    public static readonly object[] InvalidString = [MessageStrings.InvalidString];
    public static readonly object[] FileDoesNotExist = [MessageStrings.FileDoesNotExist];

    public static void UpdateInvalidString(Control target, bool isValid)
    {
        if (isValid)
        {
            DataValidationErrors.ClearErrors(target);
        }
        else
        {
            DataValidationErrors.SetErrors(target, InvalidString);
        }
    }
}
