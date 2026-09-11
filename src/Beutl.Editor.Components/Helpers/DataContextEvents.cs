using Avalonia;

namespace Beutl.Editor.Components.Helpers;

public static class DataContextEvents
{
    public static IDisposable SubscribeDataContextChange<T>(this StyledElement self, Action<T> attached, Action<T> detached)
        where T : class
    {
        T? prevContext = null;
        bool isDisposed = false;

        void AttachContext()
        {
            if (isDisposed) return;

            if (self.DataContext is T newContext && prevContext != newContext)
            {
                // Callbacks may synchronously dispose the subscription or change the context.
                prevContext = newContext;
                attached?.Invoke(newContext);
            }
        }

        void DetachContext()
        {
            if (prevContext is { } context)
            {
                prevContext = null;
                detached?.Invoke(context);
            }
        }

        void OnAttachedToLogicalTree(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
        {
            AttachContext();
        }

        void OnDetachedFromLogicalTree(object? sender, Avalonia.LogicalTree.LogicalTreeAttachmentEventArgs e)
        {
            if (isDisposed) return;

            DetachContext();
        }

        void OnDataContextChanged(object? sender, EventArgs e)
        {
            if (isDisposed) return;

            DetachContext();
            AttachContext();
        }

        self.AttachedToLogicalTree += OnAttachedToLogicalTree;
        self.DetachedFromLogicalTree += OnDetachedFromLogicalTree;
        self.DataContextChanged += OnDataContextChanged;

        AttachContext();

        return Disposable.Create(self, s =>
        {
            // An event invocation may already hold this handler when another handler disposes us.
            isDisposed = true;
            s.AttachedToLogicalTree -= OnAttachedToLogicalTree;
            s.DetachedFromLogicalTree -= OnDetachedFromLogicalTree;
            s.DataContextChanged -= OnDataContextChanged;
            DetachContext();
        });
    }
}
