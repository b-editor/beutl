namespace Beutl.Serialization;

[Flags]
public enum CoreSerializationMode
{
    Read = 1,
    Write = 1 << 1,
    ReadWrite = Read | Write,

    // Uriが設定されているCoreObjectをファイルに保存するかどうか
    // このフラグが設定されていない場合，元のドキュメントにはUriのみが保存される
    SaveReferencedObjects = 1 << 2,

    // Uriが設定されていても同じドキュメントに含めるかどうか
    EmbedReferencedObjects = 1 << 3,
}
