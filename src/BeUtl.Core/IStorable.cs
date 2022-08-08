namespace BeUtl;

// ストレージに保存可能なオブジェクト
public interface IStorable
{
    // 絶対パス
    string FileName { get; }

    // このオブジェクトが復元または保存されたときの時刻
    // 復元または保存された直後はFileNameで指定されたファイルのLastWriteTimeと同じになる
    DateTime LastSavedTime { get; }

    event EventHandler Saved;
    
    event EventHandler Restored;

    void Save(string filename);

    void Restore(string filename);
}
