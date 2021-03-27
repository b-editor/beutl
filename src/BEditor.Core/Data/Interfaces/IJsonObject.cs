using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BEditor.Data
{
    /// <summary>
    /// Jsonに保存可能なオブジェクトを表します.
    /// </summary>
    public interface IJsonObject
    {
        /// <summary>
        /// このオブジェクトのデータをJsonに書き込みます.
        /// </summary>
        /// <param name="writer">このオブジェクトのデータを書き込む <see cref="Utf8JsonWriter"/> です.</param>
        public void GetObjectData(Utf8JsonWriter writer);

        /// <summary>
        /// Jsonのデータをこのオブジェクトにセットします.
        /// </summary>
        /// <param name="element">設定するJsonのこのオブジェクトのデータ.</param>
        public void SetObjectData(JsonElement element);
    }
}
