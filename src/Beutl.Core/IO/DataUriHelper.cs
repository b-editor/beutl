namespace Beutl.Serialization;

internal static class DataUriHelper
{
    public static (byte[] Data, string Metadata) ParseDataUri(Uri uri)
    {
        // data:[<mediatype>][;base64],<data>
        string uriString = uri.OriginalString;
        int commaIndex = uriString.IndexOf(',');
        if (commaIndex < 0)
        {
            throw new FormatException("Invalid data URI format.");
        }

        string metadata = uriString[5..commaIndex]; // "data:"の後ろからカンマまで
        string dataPart = uriString[(commaIndex + 1)..];

        bool isBase64 = metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase);
        if (isBase64)
        {
            return (Convert.FromBase64String(dataPart), metadata);
        }
        else
        {
            return (System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(dataPart)), metadata);
        }
    }
}
