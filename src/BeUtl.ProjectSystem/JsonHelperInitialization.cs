using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

using BeUtl.JsonConverters;

namespace BeUtl;

internal class JsonHelperInitialization
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void AddJsonConverter()
    {
        IList<JsonConverter> converters = JsonHelper.SerializerOptions.Converters;
        converters.Add(new FileInfoConverter());
        converters.Add(new DirectoryInfoConverter());
    }
}
