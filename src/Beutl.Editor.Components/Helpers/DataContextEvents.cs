using Avalonia;

namespace Beutl.Editor.Components.Helpers;

public static class DataContextEvents
{
    public static IDisposable SubscribeDataContextChange<T>(this StyledElement self, Action<T> attached, Action<T> detached)
        where T : class
    {
        T? prevContext = null;
        bool isDisposed = false;

        void OnAttachedToLogicalTree(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
        {
            if (isDisposed) return;

            if (self.DataContext is T newContext && prevContext != newContext)
            {
                attached?.Invoke(newContext);
                prevContext = newContext;
            }
        }

        void OnDetachedFromLogicalTree(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
        {
            if (isDisposed) return;

            if (prevContext != null)
            {
                detached?.Invoke(prevContext);
                prevContext = null;
            }
        }

        void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (isDisposed) return;

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
            // An event invocation may already hold this handler when another handler disposes us.
            isDisposed = true;
            s.AttachedToLogicalTree -= OnAttachedToLogicalTree;
            s.DetachedFromLogicalTree -= OnDetachedFromLogicalTree;
            s.DataContextChanged -= OnDataContextChanged;
            if (prevContext is { } context)
            {
                prevContext = null;
                detached?.Invoke(context);
            }
        });
    }
}
