using Avalonia.Threading;
using Reactive.Bindings;

namespace Beutl.Services;

public partial class EditorService
{
    private readonly ReactivePropertySlim<ProjectLifecycleActivity> _lifecycleActivity = new();
    private readonly object _lifecycleActivitySync = new();
    private readonly List<LifecycleActivityLease> _lifecycleActivities = [];

    /// <summary>
    /// The slow project work shown in place of the editor area, or
    /// <see cref="ProjectLifecycleActivity.None"/> while the editor area is available.
    /// </summary>
    internal IReadOnlyReactiveProperty<ProjectLifecycleActivity> LifecycleActivity { get; }

    /// <summary>
    /// Shows <paramref name="activity"/> in place of the editor area until the returned handle is disposed.
    /// </summary>
    /// <remarks>
    /// Activities can overlap, as when creating a project first closes the open one, so the most recent
    /// activity still running is shown. The handle can be disposed from any thread.
    /// </remarks>
    internal IDisposable BeginLifecycleActivity(ProjectLifecycleActivity activity)
    {
        if (activity == ProjectLifecycleActivity.None)
        {
            throw new ArgumentOutOfRangeException(nameof(activity));
        }

        var lease = new LifecycleActivityLease(this, activity);
        lock (_lifecycleActivitySync)
        {
            _lifecycleActivities.Add(lease);
        }

        PublishLifecycleActivity();
        return lease;
    }

    private void EndLifecycleActivity(LifecycleActivityLease lease)
    {
        lock (_lifecycleActivitySync)
        {
            _lifecycleActivities.Remove(lease);
        }

        PublishLifecycleActivity();
    }

    private void PublishLifecycleActivity()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            _lifecycleActivity.Value = GetCurrentLifecycleActivity();
        }
        else
        {
            // The posted update reads the list when it runs, so updates arriving out of order still settle
            // on the current activity.
            Dispatcher.UIThread.Post(() => _lifecycleActivity.Value = GetCurrentLifecycleActivity());
        }
    }

    private ProjectLifecycleActivity GetCurrentLifecycleActivity()
    {
        lock (_lifecycleActivitySync)
        {
            return _lifecycleActivities.Count > 0
                ? _lifecycleActivities[^1].Activity
                : ProjectLifecycleActivity.None;
        }
    }

    private sealed class LifecycleActivityLease(
        EditorService owner,
        ProjectLifecycleActivity activity) : IDisposable
    {
        private int _disposed;

        public ProjectLifecycleActivity Activity => activity;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.EndLifecycleActivity(this);
            }
        }
    }
}
