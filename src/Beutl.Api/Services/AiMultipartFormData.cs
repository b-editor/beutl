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
                FileName = Quote(_fileName),
                FileNameStar = _fileName,
            };
            return content;
        }
    }
}
