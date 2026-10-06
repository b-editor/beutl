namespace Beutl.Services;

public partial class EditorService
{
    private readonly object _editorSuspensionSync = new();
    private readonly Dictionary<IEditorContext, (int Count, bool WasEnabled, bool WasTrackedByTab)>
        _editorSuspensions
        = new(ReferenceEqualityComparer.Instance);

    internal IDisposable SuspendEditor(IEditorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        lock (_editorSuspensionSync)
        {
            bool isTrackedByTab = TabItems.Any(
                item => ReferenceEquals(item.Context.Value, context));
            if (_editorSuspensions.TryGetValue(context, out var state))
            {
                _editorSuspensions[context] = (
                    state.Count + 1,
                    state.WasEnabled,
                    state.WasTrackedByTab || isTrackedByTab);
            }
            else
            {
                bool wasEnabled = context.IsEnabled.Value;
                _editorSuspensions.Add(context, (1, wasEnabled, isTrackedByTab));
                try
                {
                    context.IsEnabled.Value = false;
                }
                catch (Exception disableFailure)
                {
                    _editorSuspensions.Remove(context);
                    try
                    {
                        context.IsEnabled.Value = wasEnabled;
                    }
                    catch (Exception restoreFailure)
                    {
                        throw new AggregateException(disableFailure, restoreFailure);
                    }

                    throw;
                }
            }
        }

        return new EditorContextSuspensionLease(this, context);
    }

    private void EndEditorSuspension(IEditorContext context)
    {
        lock (_editorSuspensionSync)
        {
            if (!_editorSuspensions.TryGetValue(context, out var state))
            {
                return;
            }

            if (state.Count > 1)
            {
                _editorSuspensions[context] = (
                    state.Count - 1,
                    state.WasEnabled,
                    state.WasTrackedByTab);
            }
            else
            {
                _editorSuspensions.Remove(context);
                if (!state.WasTrackedByTab
                    || TabItems.Any(item => ReferenceEquals(item.Context.Value, context)))
                {
                    context.IsEnabled.Value = state.WasEnabled;
                }
            }
        }
    }

    /// <summary>
    /// Disables every open editor until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// A version-control transition holds this from before its pre-transition save until the
    /// project is closed. Releasing it earlier would let the user edit while the cycle awaits Git,
    /// and those edits would land after the safety snapshot and be discarded by the close.
    /// Suspensions share a per-context reference count with output execution, so they can complete
    /// in any order without re-enabling an editor still owned by another operation.
    /// </remarks>
    internal IDisposable SuspendEditors()
    {
        EditorTabItem[] tabItems = TabItems.ToArray();
        var suspensions = new List<IDisposable>(tabItems.Length);
        try
        {
            foreach (EditorTabItem item in tabItems)
            {
                if (item.Context.Value is { } context)
                {
                    suspensions.Add(SuspendEditor(context));
                }
            }

            return new EditorSuspension(suspensions.ToArray());
        }
        catch (Exception acquisitionFailure)
        {
            var failures = new List<Exception> { acquisitionFailure };
            foreach (IDisposable suspension in suspensions)
            {
                try
                {
                    suspension.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    failures.Add(cleanupFailure);
                }
            }

            throw new AggregateException(failures);
        }
    }

    private sealed class EditorSuspension(IDisposable[] suspensions) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            List<Exception>? failures = null;
            foreach (IDisposable suspension in suspensions)
            {
                try
                {
                    suspension.Dispose();
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is not null)
            {
                throw new AggregateException(failures);
            }
        }
    }

    private sealed class EditorContextSuspensionLease(
        EditorService owner,
        IEditorContext context) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndEditorSuspension(context);
            }
        }
    }
}
