namespace BeUtl.Validation;

public sealed class FileInfoExtensionValidator : IValidator<FileInfo?>
{
    public string[] FileExtensions { get; set; } = Array.Empty<string>();

    public FileInfo? Coerce(ICoreObject? obj, FileInfo? value)
    {
        return value;
    }

    public bool Validate(ICoreObject? obj, FileInfo? value)
    {
        if (value != null)
        {
            string extension = value.Extension;
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
