namespace BeUtl;

public interface ITopLevel : IElement
{
    string RootDirectory { get; }

    //void InvokeOnMainThread(Action action);

    //TResult InvokeOnMainThread<TResult>(Func<TResult> action);

    //Task InvokeOnMainThreadAsync(Func<Task> action);

    //Task<TResult> InvokeOnMainThreadAsync<TResult>(Func<Task<TResult>> action);
}
