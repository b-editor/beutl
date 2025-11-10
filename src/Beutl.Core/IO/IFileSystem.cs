using Beutl.Serialization;

namespace Beutl.IO;

public interface IFileSystem
{
    Uri[] GetFiles(Uri uri, string searchPattern, bool recursive);

    Uri[] GetDirectory(Uri uri, string searchPattern, bool recursive);

    bool FileExists(Uri uri);

    bool DirectoryExists(Uri uri);

    void CreateDirectory(Uri uri);

    void DeleteFile(Uri uri);

    void DeleteDirectory(Uri uri, bool recursive);

    Stream CreateFile(Uri uri);

    Stream OpenFile(Uri uri);
}

// 仮想ファイルシステム Dictionaryに保存する
