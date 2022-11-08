namespace Beutl.Validation;

public sealed class FilePathExtensionValidator : IValidator<string?>
{
    public string[] FileExtensions { get; set; } = Array.Empty<string>();

    public string? Coerce(ICoreObject? obj, string? value)
    {
        return value;
    }

    public bool Validate(ICoreObject? obj, string? value)
    {
        if (value != null)
        {
            string extension = Path.GetExtension(value);
            foreach (string item in FileExtensions)
            {
                if (extension.EndsWith(item))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
