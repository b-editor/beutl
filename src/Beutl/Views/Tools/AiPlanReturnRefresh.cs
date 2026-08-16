using Avalonia;
using Avalonia.Controls;
using Beutl.Logging;
using Beutl.Services;
using Microsoft.Extensions.Logging;

namespace Beutl.Views.Tools;

// The AI plan is managed on the website. Any control that can send the user
// there attaches this so the entitlements are re-read when the app is focused
// again; otherwise a change made in the browser — a customer portal
// cancellation in particular — stays invisible until the next manual reload.
internal static class AiPlanReturnRefresh
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(AiPlanReturnRefresh));

    public static IDisposable Attach(
        Control control,
        IAiPlanCoordinator coordinator,
        Action? refreshed = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(coordinator);
        return new Subscription(control, coordinator, refreshed);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Control _control;
        private readonly IAiPlanCoordinator _coordinator;
        private readonly Action? _refreshed;
        private readonly CancellationTokenSource _cts = new();
        private WindowBase? _window;
        private bool _disposed;
        private bool _refreshInProgress;

        public Subscription(
            Control control,
            IAiPlanCoordinator coordinator,
            Action? refreshed)
        {
            _control = control;
            _coordinator = coordinator;
            _refreshed = refreshed;
            _control.Loaded += OnLoaded;
            _control.Unloaded += OnUnloaded;
            if (_control.IsLoaded)
            {
                Subscribe();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _control.Loaded -= OnLoaded;
            _control.Unloaded -= OnUnloaded;
            Unsubscribe();
            _cts.Cancel();
            _cts.Dispose();
        }

        private void OnLoaded(object? sender, EventArgs e) => Subscribe();

        private void OnUnloaded(object? sender, EventArgs e) => Unsubscribe();

        private void Subscribe()
        {
            if (_disposed || _window is not null)
                return;

            if (TopLevel.GetTopLevel(_control) is WindowBase window)
            {
                _window = window;
                window.Activated += OnActivated;
            }
        }

        private void Unsubscribe()
        {
            if (_window is null)
                return;

            _window.Activated -= OnActivated;
            _window = null;
        }

        private async void OnActivated(object? sender, EventArgs e)
        {
            if (_disposed || _refreshInProgress)
                return;

            _refreshInProgress = true;
            try
            {
                await _coordinator.RefreshIfPendingAsync(_cts.Token);
                if (!_disposed)
                    _refreshed?.Invoke();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                s_logger.LogError(ex, "Failed to reload AI entitlements after returning to the app.");
            }
            finally
            {
                _refreshInProgress = false;
            }
        }
    }
}
