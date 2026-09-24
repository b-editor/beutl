using System.Net.Http.Headers;
using Refit;

namespace Beutl.Api.Services;

internal static class AiMultipartFormData
{
    // The server's FormData parser requires quoted disposition parameters.
    // Prepopulate the header before Refit adds this part: .NET otherwise sets
    // both filename and filename* from one string, encoding literal quotes into
    // filename* when the legacy filename parameter is prequoted.
    public static StreamPart File(Stream stream, string fileName, string mediaType, string fieldName)
        => new QuotedFilePart(stream, fileName, mediaType, fieldName);

    public static string Quote(string value)
        => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    // .NET rejects an embedded quote in the legacy filename parameter, and
    // control characters cannot occur in a valid quoted-string header. Keep
    // that parameter safe while filename* carries the original UTF-8 name.
    public static string LegacyFileName(string value)
    {
        char[] chars = value.ToCharArray();
        for (int index = 0; index < chars.Length; index++)
        {
            if (chars[index] == '"' || char.IsControl(chars[index]))
                chars[index] = '_';
        }
        return Quote(new string(chars));
    }

    private sealed class QuotedFilePart : StreamPart
    {
        private readonly string _fileName;
        private readonly string _fieldName;

        public QuotedFilePart(Stream stream, string fileName, string mediaType, string fieldName)
            : base(stream, fileName, mediaType)
        {
            _fileName = fileName;
            _fieldName = fieldName;
        }

        protected override HttpContent CreateContent()
        {
            HttpContent content = base.CreateContent();
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = Quote(_fieldName),
                FileName = LegacyFileName(_fileName),
                FileNameStar = _fileName,
            };
            return content;
        }
    }
}
