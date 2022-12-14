namespace Beutl.Validation;

public sealed class FilePathExtensionValidator : IValidator<string?>
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

    public bool TryCoerce(ValidationContext context, ref string? value)
    {
        return false;
    }

    public string? Validate(ValidationContext context, string? value)
    {
        if (value != null)
        {
            string extension = Path.GetExtension(value);
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
