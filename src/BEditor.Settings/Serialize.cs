using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace BEditor
{
    internal static class Serialize
    {
        public static T? LoadFromFile<T>(string path)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));

            try
            {
                T? obj;

                using var file = new FileStream(path, FileMode.Open);

                var serializer = new DataContractJsonSerializer(typeof(T));
                obj = (T?)serializer.ReadObject(file);

                return obj;
            }
            catch
            {
                return default;
            }
        }
        public static bool SaveToFile<T>(T obj, string path)
        {
            if (obj is null) throw new ArgumentNullException(nameof(obj));
            if (path is null) throw new ArgumentNullException(nameof(path));

            try
            {
                using var file = new FileStream(path, FileMode.Create);
                using var writer = JsonReaderWriterFactory.CreateJsonWriter(file, Encoding.UTF8, true, true, "  ");

                var serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(writer, obj);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}