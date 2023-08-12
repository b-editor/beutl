using Avalonia;

namespace Beutl;

public static class DataContextEvents
{
    public static IDisposable SubscribeDataContextChange<T>(this StyledElement self, Action<T> attached, Action<T> detached)
        where T : class
    {
        T? prevContext = null;

        void OnAttachedToLogicalTree(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
        {
            if (self.DataContext is T newContext && prevContext != newContext)
            {
                attached?.Invoke(newContext);
                prevContext = newContext;
            }
        }

        void OnDetachedFromLogicalTree(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
        {
            if (prevContext != null)
            {
                detached?.Invoke(prevContext);
                prevContext = null;
            }
        }

        void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (prevContext != null)
            {
                detached?.Invoke(prevContext);
                prevContext = null;
            }

            if (self.DataContext is T newContext && prevContext != newContext)
            {
                attached?.Invoke(newContext);
                prevContext = newContext;
            }
        }

        self.AttachedToLogicalTree += OnAttachedToLogicalTree;
        self.DetachedFromLogicalTree += OnDetachedFromLogicalTree;
        self.DataContextChanged += OnDataContextChanged;

        if (self.DataContext is T newContext && prevContext != newContext)
        {
            attached?.Invoke(newContext);
            prevContext = newContext;
        }

        return Disposable.Create(self, s =>
        {
            s.AttachedToLogicalTree -= OnAttachedToLogicalTree;
            s.DetachedFromLogicalTree -= OnDetachedFromLogicalTree;
        });
    }
}
