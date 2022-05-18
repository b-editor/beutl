namespace BeUtl;

// ストレージに保存可能なオブジェクト
public interface IStorable
{
    // 絶対パス
    string FileName { get; }

    DateTime LastSavedTime { get; }

    event EventHandler Saved;
    
    event EventHandler Restored;

    void Save(string filename);

    void Restore(string filename);
}
