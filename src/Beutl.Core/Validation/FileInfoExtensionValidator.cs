namespace Beutl.Validation;

public sealed class FileInfoExtensionValidator : IValidator<FileInfo?>
{
    private string[] _fileExtensions = Array.Empty<string>();
    private string? _display;

    public string[] FileExtensions
    {
        get => _fileExtensions;
        set
        {
            if (_fileExtensions != value)
            {
                _display = null;
                _fileExtensions = value;
            }
        }
    }

    public bool TryCoerce(ValidationContext context, ref FileInfo? value)
    {
        return false;
    }

    public string? Validate(ValidationContext context, FileInfo? value)
    {
        if (value != null)
        {
            string extension = value.Extension;
            foreach (string item in FileExtensions)
            {
                if (extension.EndsWith(item))
                {
                    return null;
                }
            }
        }

        return GetDisplay();
    }

    private string GetDisplay()
    {
        if (_display == null)
        {
            string ext = string.Join(';', _fileExtensions);
            _display = $"Please specify a file with the following extension.\n{ext}";
        }

        return _display;
    }
}
