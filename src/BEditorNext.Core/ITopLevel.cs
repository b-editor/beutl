namespace BEditorNext;

// ストレージに保存可能なオブジェクト
public interface IStorable
{
    // 絶対パス
    string FileName { get; }

    DateTime LastSavedTime { get; }

    void Save(string filename);

    void Restore(string filename);
}

public interface ITopLevel : IElement
{
    string RootDirectory { get; }

    //void InvokeOnMainThread(Action action);

    //TResult InvokeOnMainThread<TResult>(Func<TResult> action);

    //Task InvokeOnMainThreadAsync(Func<Task> action);

    //Task<TResult> InvokeOnMainThreadAsync<TResult>(Func<Task<TResult>> action);
}
