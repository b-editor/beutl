namespace BeUtl.Utilities;

public interface IWeakEventSubscriber<in TEventArgs> where TEventArgs : EventArgs
{
    void OnEvent(object? sender, WeakEvent ev, TEventArgs e);
}
