using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl;

// このインターフェイスはUIの状態を保存するのに使用します
// プロジェクトファイルの保存にはICoreSerializableを使用してください
public interface IJsonSerializable
{
    void WriteToJson(JsonObject json);

    void ReadFromJson(JsonObject json);
}
