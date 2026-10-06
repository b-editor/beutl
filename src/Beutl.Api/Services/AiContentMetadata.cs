namespace Beutl.Api.Services;

public sealed record AiContentMetadata
{
    public AiContentMetadata(string? fileName, string? contentType)
    {
        FileName = AiContentMetadataValidator.NormalizeFileName(fileName);
        ContentType = AiContentMetadataValidator.NormalizeContentType(contentType);
    }

    public string? FileName { get; }

    public string? ContentType { get; }

    public static AiContentMetadata? Combine(
        AiContentMetadata? declared,
        AiContentMetadata? downloaded)
    {
        if (declared is null)
            return downloaded;
        if (downloaded is null)
            return declared;

        if (declared.FileName is not null
            && downloaded.FileName is not null
            && !StringComparer.Ordinal.Equals(declared.FileName, downloaded.FileName))
        {
            throw new AiException("The downloaded AI content filename does not match its job metadata.");
        }

        if (declared.ContentType is not null
            && downloaded.ContentType is not null
            && !StringComparer.OrdinalIgnoreCase.Equals(declared.ContentType, downloaded.ContentType))
        {
            throw new AiException("The downloaded AI content type does not match its job metadata.");
        }

        return new AiContentMetadata(
            downloaded.FileName ?? declared.FileName,
            downloaded.ContentType ?? declared.ContentType);
    }

    public string GetFileExtension(string fallbackExtension, string requiredMediaKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredMediaKind);
        string normalizedFallback = NormalizeExtension(fallbackExtension);
        string? contentTypeExtension = ContentType?.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-matroska" => ".mkv",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/mpeg" => ".mp3",
            "audio/flac" => ".flac",
            "audio/mp4" => ".m4a",
            "audio/ogg" => ".ogg",
            "audio/webm" => ".webm",
            _ => null,
        };
        if (ContentType is not null && contentTypeExtension is null)
            throw new AiException("The AI content type is unsupported.");

        string? fileNameExtension = string.IsNullOrWhiteSpace(FileName)
            ? null
            : Path.GetExtension(FileName) is { Length: > 0 } extension
                ? NormalizeExtension(extension)
                : null;

        if (contentTypeExtension is not null
            && fileNameExtension is not null
            && !StringComparer.OrdinalIgnoreCase.Equals(contentTypeExtension, fileNameExtension)
            && !(contentTypeExtension == ".jpg" && fileNameExtension == ".jpeg"))
        {
            throw new AiException("The AI content filename and content type describe different formats.");
        }

        string result = contentTypeExtension ?? fileNameExtension ?? normalizedFallback;
        bool mediaKindMatches = requiredMediaKind switch
        {
            "image" => result is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif",
            "video" => result is ".mp4" or ".webm" or ".mov" or ".mkv",
            "audio" => result is ".wav" or ".mp3" or ".flac" or ".m4a" or ".ogg" or ".webm",
            _ => throw new ArgumentException("The required media kind is invalid.", nameof(requiredMediaKind)),
        };
        if (!mediaKindMatches)
            throw new AiException($"The AI content is not valid {requiredMediaKind} media.");
        return result;
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extension);
        string normalized = extension.StartsWith('.') ? extension : $".{extension}";
        if (normalized.Length is < 2 or > 11
            || normalized.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("The file extension is invalid.", nameof(extension));
        }

        normalized = normalized.ToLowerInvariant();
        if (normalized is not (
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif"
            or ".mp4" or ".webm" or ".mov" or ".mkv"
            or ".wav" or ".mp3" or ".flac" or ".m4a" or ".ogg"))
        {
            throw new AiException("The AI content uses an unsupported file format.");
        }

        return normalized;
    }
}

internal static class AiContentMetadataValidator
{
    public static string? NormalizeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        string normalized = fileName.Trim();
        if (normalized.Length > 255
            || normalized.Contains('/')
            || normalized.Contains('\\')
            || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || Path.IsPathRooted(normalized)
            || Path.GetFileName(normalized) != normalized
            || normalized is "." or "..")
        {
            throw new AiException("The AI response contains an invalid content filename.");
        }

        return normalized;
    }

    public static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        if (!System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(
                contentType.Trim(),
                out System.Net.Http.Headers.MediaTypeHeaderValue? parsed))
        {
            throw new AiException("The AI response contains an invalid content type.");
        }

        return parsed.MediaType;
    }
}
